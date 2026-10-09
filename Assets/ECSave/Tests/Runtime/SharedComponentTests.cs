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
    public class SharedComponentTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("SharedComponentTestWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new FactionTagModule() },
                gameDataVersion: 1,
                worldId: "SharedComponentTestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static EntityQuery AllFactionQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<FactionTag>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });

        [Test]
        public void SharedComponent_SingleValue_RoundTrip()
        {
            var em = _world.EntityManager;
            for (int i = 0; i < 5; i++)
            {
                var e = em.CreateEntity();
                em.AddSharedComponent(e, new FactionTag { FactionId = 42 });
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            _world.Dispose();
            _world = new World("SharedComponentTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(5, load.EntityCount);

            var q = AllFactionQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(5, ents.Length);
            for (int i = 0; i < ents.Length; i++)
            {
                var v = _world.EntityManager.GetSharedComponent<FactionTag>(ents[i]);
                Assert.AreEqual(42, v.FactionId);
            }

            // 同一値なので 1 Chunk にグループ化される（A-09）
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(1, chunks.Length);
        }

        [Test]
        public void SharedComponent_MultipleValues_RoundTrip()
        {
            var em = _world.EntityManager;
            var entities = new Entity[9];
            for (int i = 0; i < 9; i++)
            {
                entities[i] = em.CreateEntity();
                em.AddSharedComponent(entities[i], new FactionTag { FactionId = i % 3 });
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("SharedComponentTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = AllFactionQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(9, ents.Length);

            var counts = new Dictionary<int, int>();
            for (int i = 0; i < ents.Length; i++)
            {
                var v = _world.EntityManager.GetSharedComponent<FactionTag>(ents[i]);
                counts.TryGetValue(v.FactionId, out int c);
                counts[v.FactionId] = c + 1;
            }

            Assert.AreEqual(3, counts.Count);
            Assert.AreEqual(3, counts[0]);
            Assert.AreEqual(3, counts[1]);
            Assert.AreEqual(3, counts[2]);

            // 3 種類の値 → 3 Chunk にグループ化される
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(3, chunks.Length);
        }
    }
}
