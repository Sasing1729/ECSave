using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public class GeneratedRoundTripTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static T CreateScope<T>() where T : SaveScope
            => ScriptableObject.CreateInstance<T>();

        private static void DestroyScope(SaveScope scope)
        {
            if (scope != null) ScriptableObject.DestroyImmediate(scope);
        }

        [UnityTest]
        public IEnumerator RoundTrip_PlainComponent()
        {
            var scope = CreateScope<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                Assert.IsNotNull(sys);
                Assert.Greater(sys.Definition.Modules.Count, 0);

                var em = _world.EntityManager;
                for (int i = 0; i < 5; i++)
                {
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new GenPlayerId { Value = i + 1 });
                    em.AddComponentData(e, new GenHealth { Current = i * 10, Max = 100 });
                }

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

#if UNITY_EDITOR
                var exportPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.Desktop),
                    "ecsave_sample.sav");
                System.IO.File.WriteAllBytes(exportPath, save.Data);
                UnityEngine.Debug.Log("[ECSave] Sample saved to: " + exportPath);
#endif

                _world.Dispose();
                _world = new World("GenTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenHealth));
                var vals = q.ToComponentDataArray<GenHealth>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(5, vals.Length);

                    var seen = new HashSet<int>();
                    for (int i = 0; i < vals.Length; i++) seen.Add(vals[i].Current);
                    for (int i = 0; i < 5; i++)
                        Assert.IsTrue(seen.Contains(i * 10), $"missing {i * 10}");
                }
                finally
                {
                    vals.Dispose();
                }
            }
            finally
            {
                DestroyScope(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_EnableableComponent()
        {
            var scope = CreateScope<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var entities = new Entity[3];
                for (int i = 0; i < 3; i++)
                {
                    entities[i] = em.CreateEntity();
                    em.AddComponentData(entities[i], new GenPlayerId { Value = i + 1 });
                    em.AddComponentData(entities[i], new GenStunned { FramesRemaining = i * 10 });
                    if (i % 2 == 0)
                        em.SetComponentEnabled<GenStunned>(entities[i], false);
                }

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("GenTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenStunned>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                var restored = q.ToEntityArray(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(3, restored.Length);

                    int enabledCount = 0;
                    for (int i = 0; i < restored.Length; i++)
                        if (_world.EntityManager.IsComponentEnabled<GenStunned>(restored[i]))
                            enabledCount++;

                    Assert.AreEqual(1, enabledCount, "odd-indexed entity (i=1) should be enabled");
                }
                finally
                {
                    restored.Dispose();
                }
            }
            finally
            {
                DestroyScope(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_Buffer()
        {
            var scope = CreateScope<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                var buf = em.AddBuffer<GenInventoryItem>(e);
                buf.Add(new GenInventoryItem { ItemId = 10, Count = 1 });
                buf.Add(new GenInventoryItem { ItemId = 20, Count = 2 });
                buf.Add(new GenInventoryItem { ItemId = 30, Count = 3 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("GenTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenInventoryItem));
                var entities = q.ToEntityArray(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, entities.Length);

                    var restored = _world.EntityManager.GetBuffer<GenInventoryItem>(entities[0]);
                    Assert.AreEqual(3, restored.Length);
                    Assert.AreEqual(10, restored[0].ItemId);
                    Assert.AreEqual(20, restored[1].ItemId);
                    Assert.AreEqual(30, restored[2].ItemId);
                }
                finally
                {
                    entities.Dispose();
                }
            }
            finally
            {
                DestroyScope(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_EntityReference_Resolves()
        {
            var scope = CreateScope<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var target = em.CreateEntity();
                em.AddComponentData(target, new GenPlayerId { Value = 1 });
                em.AddComponentData(target, new GenHealth { Current = 50, Max = 100 });

                var holder = em.CreateEntity();
                em.AddComponentData(holder, new GenPlayerId { Value = 2 });
                em.AddComponentData(holder, new GenRef { Target = target, Value = 42 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("GenTestWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenRef));
                var holders = q.ToComponentDataArray<GenRef>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, holders.Length);
                    Assert.AreNotEqual(Entity.Null, holders[0].Target);
                    Assert.AreEqual(42, holders[0].Value);
                }
                finally
                {
                    holders.Dispose();
                }
            }
            finally
            {
                DestroyScope(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MultipleScopes_IndependentDefinitions()
        {
            var scopeA = CreateScope<GenTestScope>();
            var scopeB = CreateScope<GenSecondaryScope>();
            try
            {
                var sysA = scopeA.GetSaveSystem();
                var sysB = scopeB.GetSaveSystem();

                Assert.AreNotSame(sysA, sysB);
                Assert.AreNotEqual(sysA.Definition.Modules.Count, 0);
                Assert.AreNotEqual(sysB.Definition.Modules.Count, 0);
                Assert.AreEqual("GenTestWorld", sysA.Definition.WorldId);
                Assert.AreEqual("GenSecondaryWorld", sysB.Definition.WorldId);
            }
            finally
            {
                DestroyScope(scopeA);
                DestroyScope(scopeB);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator GetSaveSystem_UsesRegistryBinding()
        {
            var scope = CreateScope<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();

                bool hasHealth = false;
                for (int i = 0; i < sys.Definition.Modules.Count; i++)
                {
                    if (sys.Definition.Modules[i].FullTypeName == typeof(GenHealth).FullName)
                    {
                        hasHealth = true;
                        break;
                    }
                }
                Assert.IsTrue(hasHealth, "GenHealth module should be registered via generated binder");
                Assert.AreEqual(1, sys.Definition.Identities.Count);
            }
            finally
            {
                DestroyScope(scope);
            }

            yield return null;
        }
    }
}
