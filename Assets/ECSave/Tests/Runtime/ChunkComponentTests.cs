using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class ChunkComponentTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("ChunkTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private SaveSystem CreateSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[]
                {
                    new HealthModule(),
                    new ChunkDataModule(),
                },
                new IdentityModule[] { new PlayerIdModule() },
                1, "ChunkTestWorld");
            return new SaveSystem(def);
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("ChunkTestWorld");
        }

        private static EntityQuery ChunkDataQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<ChunkData>() },
            });

        private static void SetChunkValue(EntityManager em, Entity entity, ChunkData value)
        {
            var q = ChunkDataQuery(em);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            var entityHandle = em.GetEntityTypeHandle();
            for (int c = 0; c < chunks.Length; c++)
            {
                var chunkEntities = chunks[c].GetNativeArray(entityHandle);
                for (int i = 0; i < chunkEntities.Length; i++)
                {
                    if (chunkEntities[i] == entity)
                    {
                        em.SetChunkComponentData(chunks[c], value);
                        return;
                    }
                }
            }
        }

        [Test]
        public void SingleGroup_RoundTrip()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(
                typeof(PlayerId), typeof(Health));
            using var entities = em.CreateEntity(archetype, 5, Allocator.Temp);
            for (int i = 0; i < 5; i++)
            {
                em.SetComponentData(entities[i], new PlayerId { Value = i + 1 });
                em.SetComponentData(entities[i], new Health { Current = i * 10, Max = 100 });
            }

            em.AddChunkComponentData<ChunkData>(entities[0]);
            SetChunkValue(em, entities[0], new ChunkData { Value = 42 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var em2 = _world.EntityManager;
            var q = ChunkDataQuery(em2);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(1, chunks.Length);

            var firstEntity = chunks[0].GetNativeArray(em2.GetEntityTypeHandle())[0];
            var restored = em2.GetChunkComponentData<ChunkData>(firstEntity);
            Assert.AreEqual(42, restored.Value);
        }

        [Test]
        public void EmptyChunkComponentRegistration_NoData()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new PlayerId { Value = i + 1 });
                em.AddComponentData(e, new Health { Current = 10, Max = 20 });
            }

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = ChunkDataQuery(_world.EntityManager);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(0, chunks.Length);
        }
    }
}
