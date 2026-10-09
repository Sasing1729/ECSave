using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class EnableableEmptyBufferTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("EnableableEmptyBufferTestWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new EmptyBufferElementModule() },
                gameDataVersion: 1,
                worldId: "EnableableEmptyBufferTestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static EntityQuery AllBufferQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<EmptyBufferElement>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        [Test]
        public void ZeroSizeBuffer_MixedEnabledDisabled_RoundTrip()
        {
            var em = _world.EntityManager;

            // i=0: 1 要素 / i=1: 2 要素 / i=2: 3 要素 → 合計 6 要素
            // i=0, i=2 を Disabled → 1 enabled, 2 disabled
            var entities = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                entities[i] = em.CreateEntity();
                var buf = em.AddBuffer<EmptyBufferElement>(entities[i]);
                for (int k = 0; k <= i; k++) buf.Add(default);

                if (i % 2 == 0)
                    em.SetComponentEnabled<EmptyBufferElement>(entities[i], false);
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            _world.Dispose();
            _world = new World("EnableableEmptyBufferTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(3, load.EntityCount);

            var q = AllBufferQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length);

            int totalElements = 0;
            int enabledCount = 0, disabledCount = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                var buf = _world.EntityManager.GetBuffer<EmptyBufferElement>(ents[i]);
                totalElements += buf.Length;

                if (_world.EntityManager.IsComponentEnabled<EmptyBufferElement>(ents[i]))
                    enabledCount++;
                else
                    disabledCount++;
            }

            Assert.AreEqual(6, totalElements, "expected 1+2+3 = 6 elements");
            Assert.AreEqual(1, enabledCount, "expected 1 enabled (i=1)");
            Assert.AreEqual(2, disabledCount, "expected 2 disabled (i=0, i=2)");
        }

        [Test]
        public void ZeroSizeBuffer_AllEnabled_RoundTrip()
        {
            var em = _world.EntityManager;
            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                var buf = em.AddBuffer<EmptyBufferElement>(e);
                buf.Add(default);
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("EnableableEmptyBufferTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = AllBufferQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length);
            for (int i = 0; i < ents.Length; i++)
            {
                var buf = _world.EntityManager.GetBuffer<EmptyBufferElement>(ents[i]);
                Assert.AreEqual(1, buf.Length);
                Assert.IsTrue(_world.EntityManager.IsComponentEnabled<EmptyBufferElement>(ents[i]));
            }
        }
    }
}
