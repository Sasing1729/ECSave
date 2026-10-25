using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class EntityRefTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("EntityRefTestWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[]
                {
                    new ReferenceHolderModule(),
                    new HealthModule(),
                },
                gameDataVersion: 1,
                worldId: "EntityRefTestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void EntityRef_ToIncludedEntity_Resolves()
        {
            var em = _world.EntityManager;

            var target = em.CreateEntity();
            em.AddComponentData(target, new Health { Current = 50, Max = 100 });

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new ReferenceHolder { Target = target, Value = 42 });

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            _world.Dispose();
            _world = new World("EntityRefTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var em2 = _world.EntityManager;
            var q = em2.CreateEntityQuery(typeof(ReferenceHolder));
            using var holders = q.ToComponentDataArray<ReferenceHolder>(Allocator.Temp);
            Assert.AreEqual(1, holders.Length);
            Assert.AreNotEqual(Entity.Null, holders[0].Target);
            Assert.AreEqual(42, holders[0].Value);
        }

        [Test]
        public void EntityRef_Null_Preserved()
        {
            var em = _world.EntityManager;
            var holder = em.CreateEntity();
            em.AddComponentData(holder, new ReferenceHolder { Target = Entity.Null, Value = 7 });

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("EntityRefTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var em2 = _world.EntityManager;
            var q = em2.CreateEntityQuery(typeof(ReferenceHolder));
            using var holders = q.ToComponentDataArray<ReferenceHolder>(Allocator.Temp);
            Assert.AreEqual(1, holders.Length);
            Assert.AreEqual(Entity.Null, holders[0].Target);
            Assert.AreEqual(7, holders[0].Value);
        }

        [Test]
        public void EntityRef_ToExcludedEntity_BecomesNull()
        {
            var em = _world.EntityManager;

            var bare = em.CreateEntity();
            var holder = em.CreateEntity();
            em.AddComponentData(holder, new ReferenceHolder { Target = bare, Value = 99 });

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("EntityRefTestWorld");

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var em2 = _world.EntityManager;
            var q = em2.CreateEntityQuery(typeof(ReferenceHolder));
            using var holders = q.ToComponentDataArray<ReferenceHolder>(Allocator.Temp);
            Assert.AreEqual(1, holders.Length);
            Assert.AreEqual(Entity.Null, holders[0].Target);
            Assert.AreEqual(99, holders[0].Value);
        }
    }
}
