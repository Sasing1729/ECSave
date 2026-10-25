using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public struct SelectionRoot : IComponentData
    {
        public int Value;
    }

    public struct SelectionRef : IComponentData
    {
        public Entity Target;
        public int Value;
    }

    public sealed class SelectionTestScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<SelectionRoot>();
            b.RegisterComponent<SelectionRef>();
            b.SelectEntityReference<SelectionRef>(r => r.Target);
            b.WorldId("SelectionTestWorld");
        }
    }

    public class SelectionRoundTripTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("SelectionTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [UnityTest]
        public IEnumerator Selection_ReferencedEntity_IsSaved()
        {
            var scope = ScriptableObject.CreateInstance<SelectionTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var referenced = em.CreateEntity();
                em.AddComponentData(referenced, new SelectionRoot { Value = 42 });

                var holder = em.CreateEntity();
                em.AddComponentData(holder, new SelectionRef { Target = referenced, Value = 100 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("SelectionTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(SelectionRef));
                var holders = q.ToComponentDataArray<SelectionRef>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, holders.Length);
                    Assert.AreNotEqual(Entity.Null, holders[0].Target,
                        "Referenced entity should have been saved via SelectEntityReference");
                    Assert.AreEqual(100, holders[0].Value);
                }
                finally
                {
                    holders.Dispose();
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Selection_ReferencedEntity_HasItsComponentsRestored()
        {
            var scope = ScriptableObject.CreateInstance<SelectionTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var referenced = em.CreateEntity();
                em.AddComponentData(referenced, new SelectionRoot { Value = 42 });

                var holder = em.CreateEntity();
                em.AddComponentData(holder, new SelectionRef { Target = referenced, Value = 100 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("SelectionTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(SelectionRoot));
                var roots = q.ToComponentDataArray<SelectionRoot>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, roots.Length, "SelectionRoot should be restored");
                    Assert.AreEqual(42, roots[0].Value);
                }
                finally
                {
                    roots.Dispose();
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Selection_NullTarget_NotAdded()
        {
            var scope = ScriptableObject.CreateInstance<SelectionTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var holder = em.CreateEntity();
                em.AddComponentData(holder, new SelectionRef { Target = Entity.Null, Value = 100 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("SelectionTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(SelectionRef));
                var holders = q.ToComponentDataArray<SelectionRef>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, holders.Length);
                    Assert.AreEqual(Entity.Null, holders[0].Target);
                }
                finally
                {
                    holders.Dispose();
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Selection_MultipleRefs_AllResolved()
        {
            var scope = ScriptableObject.CreateInstance<SelectionTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var r1 = em.CreateEntity();
                em.AddComponentData(r1, new SelectionRoot { Value = 1 });
                var r2 = em.CreateEntity();
                em.AddComponentData(r2, new SelectionRoot { Value = 2 });

                var holder = em.CreateEntity();
                em.AddComponentData(holder, new SelectionRef { Target = r1, Value = 10 });

                var holder2 = em.CreateEntity();
                em.AddComponentData(holder2, new SelectionRef { Target = r2, Value = 20 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("SelectionTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(SelectionRoot));
                var roots = q.ToComponentDataArray<SelectionRoot>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(2, roots.Length,
                        "Both referenced entities should be saved and restored");
                }
                finally
                {
                    roots.Dispose();
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
