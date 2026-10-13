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
    public struct SelRoot : IComponentData
    {
        public int Value;
    }

    public struct SelRefElement : IBufferElementData
    {
        public Entity Target;
    }

    public sealed class SelBufferScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<SelRoot>();
            b.RegisterBuffer<SelRefElement>();
            b.SelectEntityReferences<SelRefElement>(e => e.Target);
            b.WorldId("SelBufferWorld");
        }
    }

    public sealed class SelMixedScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<SelRoot>();
            b.WorldId("SelMixedWorld");
        }
    }

    public class SelectionExtendedTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("SelBufferWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [UnityTest]
        public IEnumerator Selection_BufferTargets_AllResolved()
        {
            var scope = ScriptableObject.CreateInstance<SelBufferScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var r1 = em.CreateEntity();
                em.AddComponentData(r1, new SelRoot { Value = 1 });
                var r2 = em.CreateEntity();
                em.AddComponentData(r2, new SelRoot { Value = 2 });

                var holder = em.CreateEntity();
                var buf = em.AddBuffer<SelRefElement>(holder);
                buf.Add(new SelRefElement { Target = r1 });
                buf.Add(new SelRefElement { Target = r2 });
                buf.Add(new SelRefElement { Target = Entity.Null });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("SelBufferWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(SelRoot));
                var roots = q.ToComponentDataArray<SelRoot>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(2, roots.Length,
                        "Both buffer targets should be resolved and saved");
                }
                finally
                {
                    roots.Dispose();
                }

                var holderQ = _world.EntityManager.CreateEntityQuery(typeof(SelRefElement));
                var holders = holderQ.ToEntityArray(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, holders.Length);
                    var restored = _world.EntityManager.GetBuffer<SelRefElement>(holders[0]);
                    Assert.AreEqual(3, restored.Length);
                    Assert.AreNotEqual(Entity.Null, restored[0].Target);
                    Assert.AreNotEqual(Entity.Null, restored[1].Target);
                    Assert.AreEqual(Entity.Null, restored[2].Target);
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
        public IEnumerator Selection_SameEntityReferencedTwice_NotDuplicated()
        {
            var scope = ScriptableObject.CreateInstance<SelBufferScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var r = em.CreateEntity();
                em.AddComponentData(r, new SelRoot { Value = 42 });

                var h1 = em.CreateEntity();
                var buf1 = em.AddBuffer<SelRefElement>(h1);
                buf1.Add(new SelRefElement { Target = r });

                var h2 = em.CreateEntity();
                var buf2 = em.AddBuffer<SelRefElement>(h2);
                buf2.Add(new SelRefElement { Target = r });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("SelBufferWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(SelRoot));
                var roots = q.ToComponentDataArray<SelRoot>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, roots.Length,
                        "Same entity referenced twice should be saved only once");
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
        public IEnumerator Selection_NoRefs_NoExtraEntitiesSaved()
        {
            var scope = ScriptableObject.CreateInstance<SelBufferScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var holder = em.CreateEntity();
                var buf = em.AddBuffer<SelRefElement>(holder);
                buf.Add(new SelRefElement { Target = Entity.Null });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("SelBufferWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(SelRoot));
                try
                {
                    Assert.AreEqual(0, q.CalculateEntityCount());
                }
                finally
                {
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Selection_EntityAlreadyRegistered_NoDuplicateSection()
        {
            // SelRoot 自体が RegisterComponent されている Entity を選択しても
            // 論理 Entity は重複しない
            var scope = ScriptableObject.CreateInstance<SelMixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                for (int i = 0; i < 3; i++)
                {
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new SelRoot { Value = i });
                }

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("SelMixedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(3, load.EntityCount);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
