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
    public struct GenIgnored : IComponentData, IEnableableComponent
    {
        public int Value;
    }

    public sealed class GenWithoutEnableableScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenIgnored>().WithoutEnableableState();
            b.WorldId("GenWithoutEnableableWorld");
        }
    }

    public class WithoutEnableableStateTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenWithoutEnableableWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [UnityTest]
        public IEnumerator DisabledState_NotPreserved()
        {
            var scope = ScriptableObject.CreateInstance<GenWithoutEnableableScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenIgnored { Value = 42 });
                em.SetComponentEnabled<GenIgnored>(e, false);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenWithoutEnableableWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenIgnored>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                var ents = q.ToEntityArray(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, ents.Length);
                    Assert.IsTrue(
                        _world.EntityManager.IsComponentEnabled<GenIgnored>(ents[0]),
                        "Disabled state should NOT be preserved when WithoutEnableableState is used");
                }
                finally { ents.Dispose(); }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EnabledState_NotPreserved()
        {
            var scope = ScriptableObject.CreateInstance<GenWithoutEnableableScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenIgnored { Value = 7 });
                em.SetComponentEnabled<GenIgnored>(e, true);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenWithoutEnableableWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenIgnored>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                var ents = q.ToEntityArray(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, ents.Length);
                    Assert.IsTrue(_world.EntityManager.IsComponentEnabled<GenIgnored>(ents[0]));
                }
                finally { ents.Dispose(); }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
