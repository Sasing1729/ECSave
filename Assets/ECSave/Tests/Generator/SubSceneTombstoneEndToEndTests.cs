using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    [TestFixture]
    public sealed class SubSceneTombstoneEndToEndTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            SubSceneTombstone.ClearForTests();
            _world = new World("GenTombstoneWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            SubSceneTombstone.ClearForTests();
        }

        private static T CreateScope<T>() where T : SaveScope
            => ScriptableObject.CreateInstance<T>();

        [UnityTest]
        public IEnumerator MarkDestroyed_SaveLoad_EntityNotRestored()
        {
            var scope = CreateScope<GenTombstoneScope>();
            try
            {
                var em = _world.EntityManager;

                var a = em.CreateEntity();
                em.AddComponentData(a, new GenPlayerId { Value = 1 });
                em.AddComponentData(a, new GenHealth { Current = 100, Max = 100 });

                var b = em.CreateEntity();
                em.AddComponentData(b, new GenPlayerId { Value = 2 });
                em.AddComponentData(b, new GenHealth { Current = 200, Max = 200 });

                SubSceneTombstone.MarkDestroyed(em, new GenPlayerId { Value = 2 });
                em.DestroyEntity(b);

                var sys = scope.GetSaveSystem();

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("GenTombstoneWorld");

                var em2 = _world.EntityManager;
                var x = em2.CreateEntity();
                em2.AddComponentData(x, new GenPlayerId { Value = 2 });
                em2.AddComponentData(x, new GenHealth { Current = 0, Max = 0 });

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.True, $"Deserialize: {load.ErrorCode}");

                var q = em2.CreateEntityQuery(typeof(GenPlayerId));
                using var pids = q.ToComponentDataArray<GenPlayerId>(Allocator.TempJob);

                bool has1 = false, has2 = false;
                for (int i = 0; i < pids.Length; i++)
                {
                    if (pids[i].Value == 1) has1 = true;
                    if (pids[i].Value == 2) has2 = true;
                }

                Assert.That(has1, Is.True, "PlayerId=1 must be restored");
                Assert.That(has2, Is.False,
                    "PlayerId=2 was marked destroyed; it must not exist after load");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator NoMarkDestroyed_EntityNotDeleted()
        {
            var scope = CreateScope<GenTombstoneScope>();
            try
            {
                var em = _world.EntityManager;

                var a = em.CreateEntity();
                em.AddComponentData(a, new GenPlayerId { Value = 1 });
                em.AddComponentData(a, new GenHealth { Current = 100, Max = 100 });

                // 何も MarkDestroyed しない
                var sys = scope.GetSaveSystem();
                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenTombstoneWorld");

                var em2 = _world.EntityManager;
                var x = em2.CreateEntity();
                em2.AddComponentData(x, new GenPlayerId { Value = 1 });
                em2.AddComponentData(x, new GenHealth { Current = 0, Max = 0 });

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.True);

                var q = em2.CreateEntityQuery(typeof(GenPlayerId));
                Assert.That(q.CalculateEntityCount(), Is.EqualTo(1),
                    "Without tombstones, existing entities are preserved by identity match");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MarkDestroyed_RegistryRestoredAfterLoad()
        {
            var scope = CreateScope<GenTombstoneScope>();
            try
            {
                var em = _world.EntityManager;
                var b = em.CreateEntity();
                em.AddComponentData(b, new GenPlayerId { Value = 2 });
                em.AddComponentData(b, new GenHealth { Current = 200, Max = 200 });

                SubSceneTombstone.MarkDestroyed(em, new GenPlayerId { Value = 2 });
                em.DestroyEntity(b);

                var sys = scope.GetSaveSystem();
                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenTombstoneWorld");

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var em2 = _world.EntityManager;
                Assert.That(SubSceneTombstoneRegistry.TryGetSingleton(em2, out var singleton),
                    Is.True, "Registry singleton must exist after load");

                Assert.That(em2.HasBuffer<SubSceneTombstoneByte>(singleton), Is.True);
                var buf = em2.GetBuffer<SubSceneTombstoneByte>(singleton, true);
                Assert.That(buf.Length, Is.GreaterThan(0),
                    "Tombstone registry buffer must be populated after load");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MarkDestroyed_NoRegisterIdentity_NoEffect()
        {
            LogAssert.ignoreFailingMessages = true;
            var scope = CreateScope<GenTombstoneNoRegisterScope>();
            try
            {
                var em = _world.EntityManager;
                var b = em.CreateEntity();
                em.AddComponentData(b, new GenPlayerId { Value = 2 });
                em.AddComponentData(b, new GenHealth { Current = 200, Max = 200 });

                SubSceneTombstone.MarkDestroyed(em, new GenPlayerId { Value = 2 });
                em.DestroyEntity(b);

                var sys = scope.GetSaveSystem();
                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenTombstoneWorld");

                var em2 = _world.EntityManager;
                var x = em2.CreateEntity();
                em2.AddComponentData(x, new GenPlayerId { Value = 2 });
                em2.AddComponentData(x, new GenHealth { Current = 0, Max = 0 });

                // DeletionPolicy.None: 既存 Entity は削除されない。
                // Tombstone が無効（識別型未登録）でも x が削除されないことを検証。
                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var q = em2.CreateEntityQuery(typeof(GenPlayerId));
                Assert.That(q.CalculateEntityCount(), Is.EqualTo(1),
                    "Without identity registration, the tombstone cannot match; " +
                    "the existing entity must be preserved under DeletionPolicy.None");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
                LogAssert.ignoreFailingMessages = false;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MarkDestroyed_SubSceneDisabled_NoEffect()
        {
            var scope = CreateScope<GenTombstoneDisabledScope>();
            try
            {
                var em = _world.EntityManager;
                var b = em.CreateEntity();
                em.AddComponentData(b, new GenPlayerId { Value = 2 });
                em.AddComponentData(b, new GenHealth { Current = 200, Max = 200 });

                SubSceneTombstone.MarkDestroyed(em, new GenPlayerId { Value = 2 });
                em.DestroyEntity(b);

                var sys = scope.GetSaveSystem();
                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenTombstoneWorld");

                var em2 = _world.EntityManager;
                var x = em2.CreateEntity();
                em2.AddComponentData(x, new GenPlayerId { Value = 2 });
                em2.AddComponentData(x, new GenHealth { Current = 0, Max = 0 });

                // ConfigureSubScene 未オーバーライド → RegisterDestroyedEntities 無効
                // → Tombstone セクションが生成されず、P6.5 が何もしない。
                // DeletionPolicy.None で既存 Entity が残ることを検証。
                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var q = em2.CreateEntityQuery(typeof(GenPlayerId));
                Assert.That(q.CalculateEntityCount(), Is.EqualTo(1),
                    "Without RegisterDestroyedEntities, tombstones are not written; " +
                    "the existing entity must be preserved under DeletionPolicy.None");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }
    }
}
