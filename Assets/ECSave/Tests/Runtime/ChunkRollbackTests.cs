using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class ChunkRollbackTests
    {
        private World _world;

        private sealed class TestInjector : IFaultInjector
        {
            public string FailPhase;
            public bool ShouldFail(string phase) => phase == FailPhase;
        }

        private readonly TestInjector _injector = new TestInjector();

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("ChunkRollbackTestWorld");
            FaultInjector.Current = _injector;
            _injector.FailPhase = null;
        }

        [TearDown]
        public void TearDown()
        {
            FaultInjector.Current = null;
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
                1, "ChunkRollbackTestWorld");
            return new SaveSystem(def);
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("ChunkRollbackTestWorld");
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
        public void P5Failure_ChunkValueRestored()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(typeof(PlayerId), typeof(Health));
            var x = em.CreateEntity(archetype);
            em.SetComponentData(x, new PlayerId { Value = 1 });
            em.SetComponentData(x, new Health { Current = 100, Max = 200 });
            em.AddChunkComponentData<ChunkData>(x);
            SetChunkValue(em, x, new ChunkData { Value = 42 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;
            var archetype2 = em2.CreateArchetype(typeof(PlayerId), typeof(Health));

            var x2 = em2.CreateEntity(archetype2);
            em2.SetComponentData(x2, new PlayerId { Value = 1 });
            em2.SetComponentData(x2, new Health { Current = 999, Max = 999 });
            em2.AddChunkComponentData<ChunkData>(x2);
            SetChunkValue(em2, x2, new ChunkData { Value = 99 });

            _injector.FailPhase = "P5";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.WorldModificationFailed, load.ErrorCode);

            var v = _world.EntityManager.GetChunkComponentData<ChunkData>(x2);
            Assert.AreEqual(99, v.Value);
        }

        [Test]
        public void P5Failure_MultipleEntitiesInChunk_Restored()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(typeof(PlayerId), typeof(Health));
            using var entities = em.CreateEntity(archetype, 3, Allocator.Temp);
            for (int i = 0; i < 3; i++)
            {
                em.SetComponentData(entities[i], new PlayerId { Value = i + 1 });
                em.SetComponentData(entities[i], new Health { Current = 100, Max = 200 });
            }
            em.AddChunkComponentData<ChunkData>(entities[0]);
            SetChunkValue(em, entities[0], new ChunkData { Value = 42 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var archetype2 = em2.CreateArchetype(typeof(PlayerId), typeof(Health));

            using var entities2 = em2.CreateEntity(archetype2, 3, Allocator.Temp);
            for (int i = 0; i < 3; i++)
            {
                em2.SetComponentData(entities2[i], new PlayerId { Value = i + 1 });
                em2.SetComponentData(entities2[i], new Health { Current = 999, Max = 999 });
            }
            em2.AddChunkComponentData<ChunkData>(entities2[0]);
            SetChunkValue(em2, entities2[0], new ChunkData { Value = 99 });

            _injector.FailPhase = "P5";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var v = _world.EntityManager.GetChunkComponentData<ChunkData>(entities2[0]);
            Assert.AreEqual(99, v.Value);
        }

        [Test]
        public void P5Failure_AddedChunkComponentRemoved()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(typeof(PlayerId), typeof(Health));
            var x = em.CreateEntity(archetype);
            em.SetComponentData(x, new PlayerId { Value = 1 });
            em.SetComponentData(x, new Health { Current = 100, Max = 200 });
            em.AddChunkComponentData<ChunkData>(x);
            SetChunkValue(em, x, new ChunkData { Value = 42 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var archetype2 = em2.CreateArchetype(typeof(PlayerId), typeof(Health));

            var x2 = em2.CreateEntity(archetype2);
            em2.SetComponentData(x2, new PlayerId { Value = 1 });
            em2.SetComponentData(x2, new Health { Current = 999, Max = 999 });

            Assert.IsFalse(_world.EntityManager.HasChunkComponent<ChunkData>(x2));

            _injector.FailPhase = "P5";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            Assert.IsFalse(_world.EntityManager.HasChunkComponent<ChunkData>(x2));
        }

        [Test]
        public void P4Failure_ChunkValuePreserved()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(typeof(PlayerId), typeof(Health));
            var x = em.CreateEntity(archetype);
            em.SetComponentData(x, new PlayerId { Value = 1 });
            em.SetComponentData(x, new Health { Current = 100, Max = 200 });
            em.AddChunkComponentData<ChunkData>(x);
            SetChunkValue(em, x, new ChunkData { Value = 42 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var archetype2 = em2.CreateArchetype(typeof(PlayerId), typeof(Health));

            var x2 = em2.CreateEntity(archetype2);
            em2.SetComponentData(x2, new PlayerId { Value = 1 });
            em2.SetComponentData(x2, new Health { Current = 999, Max = 999 });
            em2.AddChunkComponentData<ChunkData>(x2);
            SetChunkValue(em2, x2, new ChunkData { Value = 99 });

            _injector.FailPhase = "P4";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var v = _world.EntityManager.GetChunkComponentData<ChunkData>(x2);
            Assert.AreEqual(99, v.Value);
        }

        [Test]
        public void P3Failure_NoChunkModification()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(typeof(PlayerId), typeof(Health));
            var x = em.CreateEntity(archetype);
            em.SetComponentData(x, new PlayerId { Value = 1 });
            em.SetComponentData(x, new Health { Current = 100, Max = 200 });
            em.AddChunkComponentData<ChunkData>(x);
            SetChunkValue(em, x, new ChunkData { Value = 42 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var archetype2 = em2.CreateArchetype(typeof(PlayerId), typeof(Health));

            var x2 = em2.CreateEntity(archetype2);
            em2.SetComponentData(x2, new PlayerId { Value = 1 });
            em2.SetComponentData(x2, new Health { Current = 999, Max = 999 });
            em2.AddChunkComponentData<ChunkData>(x2);
            SetChunkValue(em2, x2, new ChunkData { Value = 99 });

            _injector.FailPhase = "P3";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var v = _world.EntityManager.GetChunkComponentData<ChunkData>(x2);
            Assert.AreEqual(99, v.Value);
        }

        [Test]
        public void P5Failure_MultipleChunks_AllPreserved()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(
                ComponentType.ReadWrite<PlayerId>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.ChunkComponent<ChunkData>());

            using var entities = em.CreateEntity(archetype, 300, Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], new PlayerId { Value = i + 1 });
                em.SetComponentData(entities[i], new Health { Current = 100, Max = 200 });
            }
            SetChunkValue(em, entities[0], new ChunkData { Value = 42 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var archetype2 = em2.CreateArchetype(
                ComponentType.ReadWrite<PlayerId>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.ChunkComponent<ChunkData>());

            using var entities2 = em2.CreateEntity(archetype2, 300, Allocator.Temp);
            for (int i = 0; i < entities2.Length; i++)
            {
                em2.SetComponentData(entities2[i], new PlayerId { Value = i + 1 });
                em2.SetComponentData(entities2[i], new Health { Current = 999, Max = 999 });
            }
            SetChunkValue(em2, entities2[0], new ChunkData { Value = 99 });

            _injector.FailPhase = "P5";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var v = _world.EntityManager.GetChunkComponentData<ChunkData>(entities2[0]);
            Assert.AreEqual(99, v.Value);
        }
    }
}
