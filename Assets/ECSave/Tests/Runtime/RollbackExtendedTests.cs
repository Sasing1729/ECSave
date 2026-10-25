using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class RollbackExtendedTests
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
            _world = new World("RollbackExtTestWorld");
            FaultInjector.Current = _injector;
            _injector.FailPhase = null;
        }

        [TearDown]
        public void TearDown()
        {
            FaultInjector.Current = null;
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("RollbackExtTestWorld");
        }

        private SaveSystem CreateIdentityHealthSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "RollbackExtTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateIdentityStunnedSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new StunnedModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "RollbackExtTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateIdentityInventorySystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new InventoryElementModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "RollbackExtTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateIdentityFactionSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new FactionTagModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "RollbackExtTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateIdentityTagSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new TagComponentModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "RollbackExtTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateMultiModuleSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[]
                {
                    new HealthModule(),
                    new StunnedModule(),
                    new InventoryElementModule(),
                },
                new IdentityModule[] { new PlayerIdModule() },
                1, "RollbackExtTestWorld");
            return new SaveSystem(def);
        }

        [Test]
        public void P5Failure_BufferRestored()
        {
            var sys = CreateIdentityInventorySystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            var buf = em.AddBuffer<InventoryElement>(e);
            buf.Add(new InventoryElement { ItemId = 100, Count = 10 });
            buf.Add(new InventoryElement { ItemId = 200, Count = 20 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });
            var buf2 = em2.AddBuffer<InventoryElement>(existing);
            buf2.Add(new InventoryElement { ItemId = 999, Count = 999 });

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.WorldModificationFailed, load.ErrorCode);

            var restored = _world.EntityManager.GetBuffer<InventoryElement>(existing);
            Assert.AreEqual(1, restored.Length);
            Assert.AreEqual(999, restored[0].ItemId);
            Assert.AreEqual(999, restored[0].Count);
        }

        [Test]
        public void P5Failure_EnableableStateRestored()
        {
            var sys = CreateIdentityStunnedSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Stunned { FramesRemaining = 50 });
            em.SetComponentEnabled<Stunned>(e, false);

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });
            em2.AddComponentData(existing, new Stunned { FramesRemaining = 1 });
            em2.SetComponentEnabled<Stunned>(existing, true);

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            Assert.IsTrue(_world.EntityManager.HasComponent<Stunned>(existing));
            var s = _world.EntityManager.GetComponentData<Stunned>(existing);
            Assert.AreEqual(1, s.FramesRemaining);
            Assert.IsTrue(_world.EntityManager.IsComponentEnabled<Stunned>(existing));
        }

        [Test]
        public void P5Failure_SharedComponentRestored()
        {
            var sys = CreateIdentityFactionSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddSharedComponent(e, new FactionTag { FactionId = 42 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });
            em2.AddSharedComponent(existing, new FactionTag { FactionId = 99 });

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var v = _world.EntityManager.GetSharedComponent<FactionTag>(existing);
            Assert.AreEqual(99, v.FactionId);
        }

        [Test]
        public void P5Failure_AddedComponentRemoved()
        {
            var sys = CreateIdentityHealthSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });

            Assert.IsFalse(_world.EntityManager.HasComponent<Health>(existing));

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            Assert.IsFalse(_world.EntityManager.HasComponent<Health>(existing));
        }

        [Test]
        public void P5Failure_MultipleModules_AllRestored()
        {
            var sys = CreateMultiModuleSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });
            em.AddComponentData(e, new Stunned { FramesRemaining = 50 });
            em.SetComponentEnabled<Stunned>(e, false);
            var buf = em.AddBuffer<InventoryElement>(e);
            buf.Add(new InventoryElement { ItemId = 100, Count = 10 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });
            em2.AddComponentData(existing, new Health { Current = 999, Max = 999 });
            em2.AddComponentData(existing, new Stunned { FramesRemaining = 1 });
            em2.SetComponentEnabled<Stunned>(existing, true);
            var buf2 = em2.AddBuffer<InventoryElement>(existing);
            buf2.Add(new InventoryElement { ItemId = 999, Count = 999 });
            buf2.Add(new InventoryElement { ItemId = 888, Count = 888 });

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var h = _world.EntityManager.GetComponentData<Health>(existing);
            Assert.AreEqual(999, h.Current);
            Assert.AreEqual(999, h.Max);

            var s = _world.EntityManager.GetComponentData<Stunned>(existing);
            Assert.AreEqual(1, s.FramesRemaining);
            Assert.IsTrue(_world.EntityManager.IsComponentEnabled<Stunned>(existing));

            var restored = _world.EntityManager.GetBuffer<InventoryElement>(existing);
            Assert.AreEqual(2, restored.Length);
            Assert.AreEqual(999, restored[0].ItemId);
            Assert.AreEqual(888, restored[1].ItemId);
        }

        [Test]
        public void P5Failure_ZeroSizeComponentRemoved()
        {
            var sys = CreateIdentityTagSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new TagComponent());
            em.SetComponentEnabled<TagComponent>(e, true);

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });

            Assert.IsFalse(_world.EntityManager.HasComponent<TagComponent>(existing));

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            Assert.IsFalse(_world.EntityManager.HasComponent<TagComponent>(existing));
        }

        [Test]
        public void P5Failure_IdentityUpdateRestored()
        {
            var sys = CreateIdentityHealthSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });
            em2.AddComponentData(existing, new Health { Current = 999, Max = 888 });

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var h = _world.EntityManager.GetComponentData<Health>(existing);
            Assert.AreEqual(999, h.Current);
            Assert.AreEqual(888, h.Max);
        }

        [Test]
        public void P5Failure_EmptyWorld_RollsBackToEmpty()
        {
            var sys = CreateIdentityHealthSystem();

            var em0 = _world.EntityManager;
            var temp = em0.CreateEntity();
            em0.AddComponentData(temp, new PlayerId { Value = 1 });
            em0.AddComponentData(temp, new Health { Current = 10, Max = 20 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(0, all.Length);
        }

        [Test]
        public void P5Failure_CreatedEntitiesDestroyed()
        {
            var sys = CreateIdentityHealthSystem();
            var em = _world.EntityManager;

            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new PlayerId { Value = i + 1 });
                em.AddComponentData(e, new Health { Current = i * 10, Max = 100 });
            }

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });
            em2.AddComponentData(existing, new Health { Current = 999, Max = 999 });

            _injector.FailPhase = "P5";

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);
            Assert.AreEqual(existing, all[0]);
        }
    }
}
