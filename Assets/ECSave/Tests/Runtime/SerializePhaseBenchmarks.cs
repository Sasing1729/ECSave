using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public unsafe class SerializePhaseBenchmarks
    {
        private World _world;
        private SaveSystem _system;
        private const int EntityCount = 100_000;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("PhaseBenchmarkWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "PhaseBenchmarkWorld");
            _system = new SaveSystem(definition);

            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Health));
            using var entities = em.CreateEntity(archetype, EntityCount, Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                em.SetComponentData(entities[i], new Health { Current = i, Max = 1000 });
        }

        [TearDown]
        public void TearDown()
        {
            SerializeDiag.Enabled = false;
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void Serialize_InternalBreakdown()
        {
            for (int w = 0; w < 3; w++)
                _system.SerializeAsync(_world).GetAwaiter().GetResult();

            const int iterations = 10;

            SerializeDiag.Enabled = true;
            SerializeDiag.Reset();

            for (int it = 0; it < iterations; it++)
            {
                var result = _system.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(result.IsSuccess);
            }

            SerializeDiag.Dump($"SerializeOperation internal (Health x {EntityCount})", iterations);
            SerializeDiag.Enabled = false;
        }
    }
}
