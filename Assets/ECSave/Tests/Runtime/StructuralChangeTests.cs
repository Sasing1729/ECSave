using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class StructuralChangeTests
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
            _world = new World("StructuralChangeTestWorld");
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
            _world = new World("StructuralChangeTestWorld");
        }

        private SaveSystem CreateHealthSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "StructuralChangeTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateStunnedSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new StunnedModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "StructuralChangeTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateInventorySystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new InventoryElementModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "StructuralChangeTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateFactionSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new FactionTagModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "StructuralChangeTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateFilteredHealthSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new FilteredHealthModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, "StructuralChangeTestWorld");
            return new SaveSystem(def);
        }

        [Test]
        public void ByRegistration_RemovesComponentNotInSaveData()
        {
            var sys = CreateHealthSystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 10, Max = 20 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new PlayerId { Value = 2 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddComponentData(x, new Health { Current = 999, Max = 999 });

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 2 });
            em2.AddComponentData(y, new Health { Current = 888, Max = 888 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            Assert.IsTrue(_world.EntityManager.HasComponent<Health>(x));
            Assert.IsFalse(_world.EntityManager.HasComponent<Health>(y));
        }

        [Test]
        public void None_KeepsComponentNotInSaveData()
        {
            var sys = CreateHealthSystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 10, Max = 20 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new PlayerId { Value = 2 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddComponentData(x, new Health { Current = 999, Max = 999 });

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 2 });
            em2.AddComponentData(y, new Health { Current = 888, Max = 888 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            Assert.IsTrue(_world.EntityManager.HasComponent<Health>(y));
            var h = _world.EntityManager.GetComponentData<Health>(y);
            Assert.AreEqual(888, h.Current);
        }

        [Test]
        public void ByRegistration_RemovesBufferNotInSaveData()
        {
            var sys = CreateInventorySystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            var bufA = em.AddBuffer<InventoryElement>(a);
            bufA.Add(new InventoryElement { ItemId = 100, Count = 10 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new PlayerId { Value = 2 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddBuffer<InventoryElement>(x);

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 2 });
            var ybuf = em2.AddBuffer<InventoryElement>(y);
            ybuf.Add(new InventoryElement { ItemId = 999, Count = 999 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            Assert.IsTrue(_world.EntityManager.HasComponent<InventoryElement>(x));
            Assert.IsFalse(_world.EntityManager.HasComponent<InventoryElement>(y));
        }

        [Test]
        public void ByRegistration_RemovesSharedNotInSaveData()
        {
            var sys = CreateFactionSystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddSharedComponent(a, new FactionTag { FactionId = 42 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new PlayerId { Value = 2 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddSharedComponent(x, new FactionTag { FactionId = 999 });

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 2 });
            em2.AddSharedComponent(y, new FactionTag { FactionId = 888 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            Assert.IsTrue(_world.EntityManager.HasComponent<FactionTag>(x));
            Assert.IsFalse(_world.EntityManager.HasComponent<FactionTag>(y));
        }

        [Test]
        public void ByRegistration_RemovesEnableableNotInSaveData()
        {
            var sys = CreateStunnedSystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Stunned { FramesRemaining = 10 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new PlayerId { Value = 2 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddComponentData(x, new Stunned { FramesRemaining = 999 });

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 2 });
            em2.AddComponentData(y, new Stunned { FramesRemaining = 888 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            Assert.IsTrue(_world.EntityManager.HasComponent<Stunned>(x));
            Assert.IsFalse(_world.EntityManager.HasComponent<Stunned>(y));
        }

        [Test]
        public void ByRegistration_FilteredOutEntity_KeepsComponent()
        {
            var sys = CreateFilteredHealthSystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponent<PlayerOnlyFilter>(a);
            em.AddComponentData(a, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddComponent<PlayerOnlyFilter>(x);
            em2.AddComponentData(x, new Health { Current = 999, Max = 999 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            Assert.IsTrue(_world.EntityManager.HasComponent<Health>(x));
            var h = _world.EntityManager.GetComponentData<Health>(x);
            Assert.AreEqual(10, h.Current);
        }

        [Test]
        public void ByScope_RemovesUncoveredComponents()
        {
            var sys = CreateHealthSystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddComponentData(x, new Health { Current = 999, Max = 999 });

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 2 });
            em2.AddComponentData(y, new Health { Current = 888, Max = 888 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);
            Assert.IsTrue(_world.EntityManager.HasComponent<Health>(all[0]));
        }
    }
}
