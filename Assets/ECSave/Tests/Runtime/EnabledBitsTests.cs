using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// Phase 5b：Enableable 状態のラウンドトリップ（I-02 の最小版）。
    ///
    /// 【注意】検証用クエリには必ず IgnoreComponentEnabledState を指定する。
    /// A-06：デフォルトクエリは無効 Entity を除外するため、
    /// 有効状態の検証には使えない。
    /// </summary>
    public class EnabledBitsTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("EnabledBitsTestWorld");

            var definition = new SaveDefinition(
                new SaveTypeModule[] { new StunnedModule() },
                gameDataVersion: 1,
                worldId: "EnabledBitsTestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        /// <summary>
        /// 有効・無効の両方を含む Stunned を取得するクエリ。
        /// A-06：IgnoreComponentEnabledState を指定。
        /// </summary>
        private static EntityQuery BuildAllStunnedQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Stunned>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        [Test]
        public void EnabledAndDisabled_RoundTrip()
        {
            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Stunned));

            // 5 件作成、うち偶数番目を Disabled に
            var entities = new Entity[5];
            for (int i = 0; i < 5; i++)
            {
                entities[i] = em.CreateEntity(archetype);
                em.SetComponentData(entities[i], new Stunned { FramesRemaining = i * 10 });
                if (i % 2 == 0) em.SetComponentEnabled<Stunned>(entities[i], false);
            }

            // 期待値を記録
            var expected = new System.Collections.Generic.Dictionary<int, bool>();
            for (int i = 0; i < 5; i++)
                expected[i * 10] = em.IsComponentEnabled<Stunned>(entities[i]);

            // Serialize → World 再構築 → Deserialize
            var saveResult = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(saveResult.IsSuccess, $"Serialize failed: {saveResult.ErrorCode}");

            _world.Dispose();
            _world = new World("EnabledBitsTestWorld");

            var loadResult = _system
                .DeserializeAsync(_world, saveResult.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(loadResult.IsSuccess, $"Deserialize failed: {loadResult.ErrorCode}");

            // 復元された Entity の値と有効状態を検証
            // ▼ 修正：IgnoreComponentEnabledState を指定したクエリを使う
            var q = BuildAllStunnedQuery(_world.EntityManager);
            using var vals = q.ToComponentDataArray<Stunned>(Allocator.Temp);
            using var ents = q.ToEntityArray(Allocator.Temp);

            var actual = new System.Collections.Generic.Dictionary<int, bool>();
            for (int i = 0; i < vals.Length; i++)
                actual[vals[i].FramesRemaining] = _world.EntityManager
                    .IsComponentEnabled<Stunned>(ents[i]);

            Assert.AreEqual(expected.Count, actual.Count,
                $"count mismatch: expected={expected.Count}, actual={actual.Count}");
            foreach (var kv in expected)
            {
                Assert.IsTrue(actual.ContainsKey(kv.Key),
                    $"missing FramesRemaining={kv.Key}");
                Assert.AreEqual(kv.Value, actual[kv.Key],
                    $"enabled mismatch for FramesRemaining={kv.Key}");
            }
        }

        [Test]
        public void AllEnabled_RoundTrip()
        {
            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Stunned));
            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity(archetype);
                em.SetComponentData(e, new Stunned { FramesRemaining = 100 });
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("EnabledBitsTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            // ▼ 修正：IgnoreComponentEnabledState を指定（念のため統一）
            var q = BuildAllStunnedQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length);
            for (int i = 0; i < ents.Length; i++)
                Assert.IsTrue(_world.EntityManager.IsComponentEnabled<Stunned>(ents[i]));
        }

        [Test]
        public void AllDisabled_RoundTrip()
        {
            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Stunned));
            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity(archetype);
                em.SetComponentData(e, new Stunned { FramesRemaining = i });
                em.SetComponentEnabled<Stunned>(e, false);
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("EnabledBitsTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            // ▼ 修正：IgnoreComponentEnabledState を指定
            // これが無いと 0 件になり、ループが回らず誤って Pass してしまう。
            var q = BuildAllStunnedQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length,
                "IgnoreComponentEnabledState missing: disabled entities are excluded");
            for (int i = 0; i < ents.Length; i++)
                Assert.IsFalse(_world.EntityManager.IsComponentEnabled<Stunned>(ents[i]),
                    $"entity {i} should be disabled");
        }
    }
}
