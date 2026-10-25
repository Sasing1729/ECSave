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
    /// Phase 5c-2：DynamicBuffer のラウンドトリップ（I-05 の最小版）。
    /// </summary>
    public class BufferTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("BufferTestWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new InventoryElementModule() },
                gameDataVersion: 1,
                worldId: "BufferTestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static EntityQuery AllInventoryQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<InventoryElement>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        [Test]
        public void Buffer_RoundTrip_PreservesOrderAndValues()
        {
            var em = _world.EntityManager;

            // Entity A: 3 要素、Entity B: 2 要素、Entity C: 0 要素（空）
            var eA = em.CreateEntity();
            var bufA = em.AddBuffer<InventoryElement>(eA);
            bufA.Add(new InventoryElement { ItemId = 1, Count = 10 });
            bufA.Add(new InventoryElement { ItemId = 2, Count = 20 });
            bufA.Add(new InventoryElement { ItemId = 3, Count = 30 });

            var eB = em.CreateEntity();
            var bufB = em.AddBuffer<InventoryElement>(eB);
            bufB.Add(new InventoryElement { ItemId = 100, Count = 1000 });
            bufB.Add(new InventoryElement { ItemId = 200, Count = 2000 });

            var eC = em.CreateEntity();
            em.AddBuffer<InventoryElement>(eC); // 空

            // Serialize
            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            // World 再構築
            _world.Dispose();
            _world = new World("BufferTestWorld");

            // Deserialize
            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(3, load.EntityCount);

            // 検証：Buffer の内容を値で照合（順序は保持されるべき）
            var q = AllInventoryQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, entities.Length);

            var allBuffers = new List<List<(int, int)>>();
            for (int i = 0; i < entities.Length; i++)
            {
                var buf = _world.EntityManager.GetBuffer<InventoryElement>(entities[i]);
                var list = new List<(int, int)>();
                for (int k = 0; k < buf.Length; k++)
                    list.Add((buf[k].ItemId, buf[k].Count));
                allBuffers.Add(list);
            }

            // 期待される3つの集合
            bool hasA = allBuffers.Exists(b => b.Count == 3
                && b[0] == (1, 10) && b[1] == (2, 20) && b[2] == (3, 30));
            bool hasB = allBuffers.Exists(b => b.Count == 2
                && b[0] == (100, 1000) && b[1] == (200, 2000));
            bool hasC = allBuffers.Exists(b => b.Count == 0);

            Assert.IsTrue(hasA, "missing buffer A (3 elements)");
            Assert.IsTrue(hasB, "missing buffer B (2 elements)");
            Assert.IsTrue(hasC, "missing buffer C (empty)");
        }

        [Test]
        public void Buffer_EmptyBuffers_RoundTrip()
        {
            var em = _world.EntityManager;
            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                em.AddBuffer<InventoryElement>(e);
            }

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("BufferTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(3, load.EntityCount);

            var q = AllInventoryQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                var buf = _world.EntityManager.GetBuffer<InventoryElement>(entities[i]);
                Assert.AreEqual(0, buf.Length);
            }
        }

        [Test]
        public void Buffer_ManyElements_RoundTrip()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            var buf = em.AddBuffer<InventoryElement>(e);
            const int count = 200;
            for (int i = 0; i < count; i++)
                buf.Add(new InventoryElement { ItemId = i, Count = i * 2 });

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("BufferTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = AllInventoryQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);

            var restored = _world.EntityManager.GetBuffer<InventoryElement>(entities[0]);
            Assert.AreEqual(count, restored.Length);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(i, restored[i].ItemId, $"ItemId mismatch at {i}");
                Assert.AreEqual(i * 2, restored[i].Count, $"Count mismatch at {i}");
            }
        }
    }
}
