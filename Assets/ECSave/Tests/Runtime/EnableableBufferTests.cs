using System.Collections.Generic;
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
    /// Phase 5c-3：Enableable Buffer のラウンドトリップ（I-02 の Buffer 部分）。
    ///
    /// 【注意】検証用クエリには必ず IgnoreComponentEnabledState を指定する。
    /// A-06：デフォルトクエリは無効 Entity を除外するため、有効状態の検証には使えない。
    /// </summary>
    public class EnableableBufferTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("EnableableBufferTestWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new StatusEffectModule() },
                gameDataVersion: 1,
                worldId: "EnableableBufferTestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static EntityQuery AllStatusEffectQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<StatusEffect>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        [Test]
        public void EnableableBuffer_Mixed_RoundTrip()
        {
            var em = _world.EntityManager;

            // 5 Entity、うち偶数番目を Disabled
            var entities = new Entity[5];
            for (int i = 0; i < 5; i++)
            {
                entities[i] = em.CreateEntity();
                var buf = em.AddBuffer<StatusEffect>(entities[i]);
                buf.Add(new StatusEffect { EffectId = i * 10, Duration = 100 + i });
                buf.Add(new StatusEffect { EffectId = i * 10 + 1, Duration = 200 + i });

                if (i % 2 == 0)
                    em.SetComponentEnabled<StatusEffect>(entities[i], false);
            }

            // 期待値
            var expected = new Dictionary<int, bool>();
            for (int i = 0; i < 5; i++)
                expected[i * 10] = em.IsComponentEnabled<StatusEffect>(entities[i]);

            // Serialize
            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            // World 再構築
            _world.Dispose();
            _world = new World("EnableableBufferTestWorld");

            // Deserialize
            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(5, load.EntityCount);

            // 検証
            var q = AllStatusEffectQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(5, ents.Length, "IgnoreComponentEnabledState missing?");

            var actual = new Dictionary<int, bool>();
            for (int i = 0; i < ents.Length; i++)
            {
                var buf = _world.EntityManager.GetBuffer<StatusEffect>(ents[i]);
                Assert.AreEqual(2, buf.Length);

                int key = buf[0].EffectId;
                actual[key] = _world.EntityManager.IsComponentEnabled<StatusEffect>(ents[i]);
            }

            Assert.AreEqual(expected.Count, actual.Count);
            foreach (var kv in expected)
            {
                Assert.IsTrue(actual.ContainsKey(kv.Key), $"missing EffectId={kv.Key}");
                Assert.AreEqual(kv.Value, actual[kv.Key],
                    $"enabled mismatch for EffectId={kv.Key}");
            }
        }

        [Test]
        public void EnableableBuffer_AllEnabled_RoundTrip()
        {
            var em = _world.EntityManager;
            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                var buf = em.AddBuffer<StatusEffect>(e);
                buf.Add(new StatusEffect { EffectId = i, Duration = 10 });
            }

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("EnableableBufferTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = AllStatusEffectQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length);
            for (int i = 0; i < ents.Length; i++)
                Assert.IsTrue(_world.EntityManager.IsComponentEnabled<StatusEffect>(ents[i]));
        }

        [Test]
        public void EnableableBuffer_AllDisabled_RoundTrip()
        {
            var em = _world.EntityManager;
            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                var buf = em.AddBuffer<StatusEffect>(e);
                buf.Add(new StatusEffect { EffectId = i, Duration = 10 });
                em.SetComponentEnabled<StatusEffect>(e, false);
            }

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("EnableableBufferTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = AllStatusEffectQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length,
                "IgnoreComponentEnabledState missing: disabled entities excluded");
            for (int i = 0; i < ents.Length; i++)
                Assert.IsFalse(_world.EntityManager.IsComponentEnabled<StatusEffect>(ents[i]));
        }

        [Test]
        public void EnableableBuffer_Empty_RoundTrip()
        {
            var em = _world.EntityManager;

            // 3 Entity、Buffer は空、有効状態は混在
            var entities = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                entities[i] = em.CreateEntity();
                em.AddBuffer<StatusEffect>(entities[i]); // 空
                if (i % 2 == 0)
                    em.SetComponentEnabled<StatusEffect>(entities[i], false);
            }

            var expected = new Dictionary<int, bool>();
            for (int i = 0; i < 3; i++)
                expected[i] = em.IsComponentEnabled<StatusEffect>(entities[i]);

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            _world.Dispose();
            _world = new World("EnableableBufferTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = AllStatusEffectQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length);
            for (int i = 0; i < ents.Length; i++)
            {
                var buf = _world.EntityManager.GetBuffer<StatusEffect>(ents[i]);
                Assert.AreEqual(0, buf.Length);
            }
        }
    }
}
