using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class RollbackTests
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
            _world = new World("RollbackTestWorld");
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
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "RollbackTestWorld");
            return new SaveSystem(def);
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("RollbackTestWorld");
        }

        private static EntityQuery HealthQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Health>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        [Test]
        public void P3Failure_RollsBackCompletely()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new Health { Current = 100, Max = 200 });

            _injector.FailPhase = "P3";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.WorldModificationFailed, load.ErrorCode);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);

            var h = _world.EntityManager.GetComponentData<Health>(all[0]);
            Assert.AreEqual(100, h.Current);
            Assert.AreEqual(200, h.Max);
        }

        [Test]
        public void P4Failure_RollsBackValues()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new Health { Current = 100, Max = 200 });

            _injector.FailPhase = "P4";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.WorldModificationFailed, load.ErrorCode);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);

            var h = _world.EntityManager.GetComponentData<Health>(all[0]);
            Assert.AreEqual(100, h.Current);
            Assert.AreEqual(200, h.Max);
        }

        [Test]
        public void P7Failure_RollsBackBeforeDestroy()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var extra = em2.CreateEntity();
            em2.AddComponentData(extra, new Health { Current = 500, Max = 600 });

            _injector.FailPhase = "P7";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);

            var h = _world.EntityManager.GetComponentData<Health>(all[0]);
            Assert.AreEqual(500, h.Current);
        }

        [Test]
        public void NoFailure_CommitsSuccessfully()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);

            var h = _world.EntityManager.GetComponentData<Health>(entities[0]);
            Assert.AreEqual(10, h.Current);
            Assert.AreEqual(20, h.Max);
        }

        [Test]
        public void P4Failure_MultipleExistingEntities_RolledBack()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            for (int i = 0; i < 5; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new Health { Current = i * 10, Max = 100 });
            }

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            for (int i = 0; i < 5; i++)
            {
                var e = em2.CreateEntity();
                em2.AddComponentData(e, new Health { Current = 999, Max = 999 });
            }

            _injector.FailPhase = "P4";

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(5, entities.Length);

            for (int i = 0; i < entities.Length; i++)
            {
                var h = _world.EntityManager.GetComponentData<Health>(entities[i]);
                Assert.AreEqual(999, h.Current);
                Assert.AreEqual(999, h.Max);
            }
        }
    }
}
