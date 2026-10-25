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
    public class LoadEdgeCaseTests
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

        private static GenTestScope CreateScope()
            => ScriptableObject.CreateInstance<GenTestScope>();

        [UnityTest]
        public IEnumerator EmptyRestorationScope_LoadSucceeds_NoUpdates()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var originalEntities = new Entity[3];
                for (int i = 0; i < 3; i++)
                {
                    originalEntities[i] = em.CreateEntity();
                    em.AddComponentData(originalEntities[i],
                        new GenHealth { Current = i * 10, Max = 100 });
                }

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var emptyQuery = em.CreateEntityQuery(typeof(GenStunned));

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.Custom(emptyQuery), DeletionPolicy.ByScope)
                    .GetAwaiter().GetResult();

                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(_world.EntityManager.Exists(originalEntities[i]),
                        $"Original entity {i} should not be deleted with empty scope");
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ZeroEntities_SaveSucceeds_LoadProducesZero()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);
                Assert.IsNotNull(save.Data);
                Assert.Greater(save.Data.Length, 0);

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
                Assert.AreEqual(0, load.EntityCount);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SameDataLoadedTwice_WithNone_DuplicatesEntities()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 42, Max = 100 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenTestWorld");

                // 1回目
                var load1 = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load1.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenHealth));
                int count1 = q.CalculateEntityCount();
                Assert.AreEqual(1, count1);

                // 2回目（識別型なしなので新規生成される）
                var load2 = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load2.IsSuccess);

                int count2 = q.CalculateEntityCount();
                Assert.AreEqual(2, count2,
                    "Same SaveData loaded twice with None and no identity should duplicate");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SameDataLoadedTwice_WithIdentity_NoDuplication()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                em.AddComponentData(e, new GenHealth { Current = 42, Max = 100 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenTestWorld");

                var em2 = _world.EntityManager;
                var existing = em2.CreateEntity();
                em2.AddComponentData(existing, new GenPlayerId { Value = 1 });
                em2.AddComponentData(existing, new GenHealth { Current = 0, Max = 0 });

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenHealth));
                Assert.AreEqual(1, q.CalculateEntityCount(),
                    "Identity match should reuse existing entity, not duplicate");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
