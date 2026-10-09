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
    public class EnableableTagTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("EnableableTagTestWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new TagComponentModule() },
                gameDataVersion: 1,
                worldId: "EnableableTagTestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static EntityQuery AllTagQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TagComponent>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        [Test]
        public void ZeroSizeTag_MixedEnabledDisabled_RoundTrip()
        {
            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(TagComponent));

            // i = 0, 2, 4 を Disabled（合計: 3 disabled, 2 enabled）
            var entities = new Entity[5];
            for (int i = 0; i < 5; i++)
            {
                entities[i] = em.CreateEntity(archetype);
                if (i % 2 == 0)
                    em.SetComponentEnabled<TagComponent>(entities[i], false);
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            _world.Dispose();
            _world = new World("EnableableTagTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(5, load.EntityCount);

            var q = AllTagQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(5, ents.Length);

            int enabledCount = 0, disabledCount = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (_world.EntityManager.IsComponentEnabled<TagComponent>(ents[i]))
                    enabledCount++;
                else
                    disabledCount++;
            }

            Assert.AreEqual(2, enabledCount, "expected 2 enabled (i=1, i=3)");
            Assert.AreEqual(3, disabledCount, "expected 3 disabled (i=0, i=2, i=4)");
        }

        [Test]
        public void ZeroSizeTag_AllDisabled_RoundTrip()
        {
            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(TagComponent));
            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity(archetype);
                em.SetComponentEnabled<TagComponent>(e, false);
            }

            var save = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            _world.Dispose();
            _world = new World("EnableableTagTestWorld");

            var load = _system.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = AllTagQuery(_world.EntityManager);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(3, ents.Length);
            for (int i = 0; i < ents.Length; i++)
                Assert.IsFalse(_world.EntityManager.IsComponentEnabled<TagComponent>(ents[i]));
        }
    }
}
