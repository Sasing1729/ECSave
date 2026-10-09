using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class DeletionPolicyTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("DeletionTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private SaveSystem CreateSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "DeletionTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateFilteredSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new FilteredHealthModule() },
                gameDataVersion: 1,
                worldId: "DeletionTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateIdentitySystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                new IdentityModule[] { new PlayerIdModule() },
                gameDataVersion: 1,
                worldId: "DeletionTestWorld");
            return new SaveSystem(def);
        }

        private static EntityQuery HealthQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Health>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("DeletionTestWorld");
        }

        [Test]
        public void None_KeepsUnmatchedExistingEntities()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra, new Health { Current = 999, Max = 999 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(2, entities.Length);
        }

        [Test]
        public void ByRegistration_DeletesUnmatchedWithRegisteredType()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra, new Health { Current = 999, Max = 999 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);
        }

        [Test]
        public void ByRegistration_KeepsUnmatchedWithoutRegisteredType()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra = _world.EntityManager.CreateEntity();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(2, all.Length);
        }

        [Test]
        public void ByScope_DeletesAllUnmatched()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra1 = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra1, new Health { Current = 999, Max = 999 });
            var extra2 = _world.EntityManager.CreateEntity();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);
        }

        [Test]
        public void CustomRegion_OnlyDeletesWithinRegion()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra1 = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra1, new Health { Current = 999, Max = 999 });
            var extra2 = _world.EntityManager.CreateEntity();

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            var scope = RestorationScope.Custom(q);

            var load = sys.DeserializeAsync(_world, save.Data,
                scope, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(2, all.Length);
        }

        [Test]
        public void None_WithCustomRegion_KeepsAll()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra, new Health { Current = 999, Max = 999 });

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            var scope = RestorationScope.Custom(q);

            var load = sys.DeserializeAsync(_world, save.Data,
                scope, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var hq = HealthQuery(_world.EntityManager);
            using var entities = hq.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(2, entities.Length);
        }

        [Test]
        public void CustomRegion_ByRegistration_DeletesWithinRegionOnly()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var inside = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(inside, new Health { Current = 999, Max = 999 });
            _world.EntityManager.AddComponent<PlayerOnlyFilter>(inside);

            var outside = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(outside, new Health { Current = 888, Max = 888 });

            var q = _world.EntityManager.CreateEntityQuery(typeof(PlayerOnlyFilter));
            var scope = RestorationScope.Custom(q);

            var load = sys.DeserializeAsync(_world, save.Data,
                scope, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var hq = HealthQuery(_world.EntityManager);
            using var entities = hq.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(2, entities.Length);
        }

        [Test]
        public void EmptyRegion_NoDeletion()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra, new Health { Current = 999, Max = 999 });

            var q = _world.EntityManager.CreateEntityQuery(typeof(PlayerOnlyFilter));
            var scope = RestorationScope.Custom(q);

            var load = sys.DeserializeAsync(_world, save.Data,
                scope, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var hq = HealthQuery(_world.EntityManager);
            using var entities = hq.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(2, entities.Length);
        }

        [Test]
        public void EmptySaveData_ByScope_DeletesAll()
        {
            var sys = CreateSystem();

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var e1 = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(e1, new Health { Current = 1, Max = 1 });
            var e2 = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(e2, new Health { Current = 2, Max = 2 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(0, all.Length);
        }

        [Test]
        public void EmptySaveData_None_KeepsAll()
        {
            var sys = CreateSystem();

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var e1 = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(e1, new Health { Current = 1, Max = 1 });
            var e2 = _world.EntityManager.CreateEntity();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(2, all.Length);
        }

        [Test]
        public void NoDeletionTargets_ByScope_NoOp()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);
        }

        [Test]
        public void Prefab_NotDeleted_EntireWorld()
        {
            var sys = CreateSystem();

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var prefab = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(prefab, new Health { Current = 999, Max = 999 });
            _world.EntityManager.AddComponent<Prefab>(prefab);

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length);
            Assert.IsTrue(_world.EntityManager.HasComponent<Prefab>(all[0]));
        }

        [Test]
        public void Disabled_DeletedByScope()
        {
            var sys = CreateSystem();

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var disabled = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(disabled, new Health { Current = 999, Max = 999 });
            _world.EntityManager.AddComponent<Disabled>(disabled);

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(0, all.Length);
        }

        [Test]
        public void FilteredOutEntity_NotDeletedByRegistration()
        {
            var sys = CreateFilteredSystem();
            var em = _world.EntityManager;

            var saved = em.CreateEntity();
            em.AddComponentData(saved, new Health { Current = 100, Max = 200 });
            em.AddComponent<PlayerOnlyFilter>(saved);

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();

            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra, new Health { Current = 999, Max = 999 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(2, entities.Length);
        }

        [Test]
        public void IdentityUpdateTargets_NotDeletedByScope()
        {
            var sys = CreateIdentitySystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new PlayerId { Value = 2 });
            em.AddComponentData(b, new Health { Current = 200, Max = 300 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddComponentData(x, new Health { Current = 0, Max = 0 });

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 2 });
            em2.AddComponentData(y, new Health { Current = 0, Max = 0 });

            var z = em2.CreateEntity();
            em2.AddComponentData(z, new PlayerId { Value = 3 });
            em2.AddComponentData(z, new Health { Current = 999, Max = 999 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(2, entities.Length);

            var pidQ = _world.EntityManager.CreateEntityQuery(typeof(PlayerId));
            using var pids = pidQ.ToComponentDataArray<PlayerId>(Allocator.Temp);
            bool has1 = false, has2 = false, has3 = false;
            for (int i = 0; i < pids.Length; i++)
            {
                if (pids[i].Value == 1) has1 = true;
                if (pids[i].Value == 2) has2 = true;
                if (pids[i].Value == 3) has3 = true;
            }
            Assert.IsTrue(has1);
            Assert.IsTrue(has2);
            Assert.IsFalse(has3);
        }

        [Test]
        public void IdentityUpdateTargets_NotDeletedByRegistration()
        {
            var sys = CreateIdentitySystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            var x = em2.CreateEntity();
            em2.AddComponentData(x, new PlayerId { Value = 1 });
            em2.AddComponentData(x, new Health { Current = 0, Max = 0 });

            var y = em2.CreateEntity();
            em2.AddComponentData(y, new PlayerId { Value = 99 });
            em2.AddComponentData(y, new Health { Current = 999, Max = 999 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);
            Assert.AreEqual(100, _world.EntityManager.GetComponentData<Health>(entities[0]).Current);
        }
    }
}
