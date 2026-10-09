using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class IdentityMatchTests
    {
        private World _world;

        private SaveSystem CreateSystemWithBoth()
        {
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                new IdentityModule[] { new PlayerIdModule(), new EnemyIdModule() },
                gameDataVersion: 1,
                worldId: "IdentityTestWorld");
            return new SaveSystem(definition);
        }

        private SaveSystem CreateSystemWithPlayerOnly()
        {
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                new IdentityModule[] { new PlayerIdModule() },
                gameDataVersion: 1,
                worldId: "IdentityTestWorld");
            return new SaveSystem(definition);
        }

        private SaveSystem CreateSystemWithHealthOnly()
        {
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "IdentityTestWorld");
            return new SaveSystem(definition);
        }

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("IdentityTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
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
            _world = new World("IdentityTestWorld");
        }

        [Test]
        public void Identity_MatchExistingEntity_UpdatesInsteadOfCreating()
        {
            var sys = CreateSystemWithPlayerOnly();
            var em = _world.EntityManager;

            var player = em.CreateEntity();
            em.AddComponentData(player, new PlayerId { Value = 1 });
            em.AddComponentData(player, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;
            var existing = em2.CreateEntity();
            em2.AddComponentData(existing, new PlayerId { Value = 1 });
            em2.AddComponentData(existing, new Health { Current = 0, Max = 0 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);
            Assert.AreEqual(existing, entities[0]);

            var health = _world.EntityManager.GetComponentData<Health>(entities[0]);
            Assert.AreEqual(100, health.Current);
            Assert.AreEqual(200, health.Max);
        }

        [Test]
        public void Identity_NoMatch_CreatesNewEntity()
        {
            var sys = CreateSystemWithPlayerOnly();
            var em = _world.EntityManager;

            var player = em.CreateEntity();
            em.AddComponentData(player, new PlayerId { Value = 5 });
            em.AddComponentData(player, new Health { Current = 50, Max = 100 });

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

            var pidQ = _world.EntityManager.CreateEntityQuery(typeof(PlayerId));
            using var pidEntities = pidQ.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, pidEntities.Length);
            var pid = _world.EntityManager.GetComponentData<PlayerId>(pidEntities[0]);
            Assert.AreEqual(5, pid.Value);
        }

        [Test]
        public void Identity_WithoutIdentities_AlwaysCreatesNew()
        {
            var sys = CreateSystemWithHealthOnly();

            var em = _world.EntityManager;
            var player = em.CreateEntity();
            em.AddComponentData(player, new PlayerId { Value = 1 });
            em.AddComponentData(player, new Health { Current = 100, Max = 200 });

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
        }

        [Test]
        public void Identity_MultipleIdentityTypes_BothMatch()
        {
            var sys = CreateSystemWithBoth();
            var em = _world.EntityManager;

            var x = em.CreateEntity();
            em.AddComponentData(x, new PlayerId { Value = 42 });
            em.AddComponentData(x, new EnemyId { Value = 7 });
            em.AddComponentData(x, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var em2 = _world.EntityManager;
            var w = em2.CreateEntity();
            em2.AddComponentData(w, new PlayerId { Value = 42 });
            em2.AddComponentData(w, new EnemyId { Value = 7 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);
            Assert.AreEqual(w, entities[0]);

            Assert.IsTrue(_world.EntityManager.HasComponent<PlayerId>(w));
            Assert.IsTrue(_world.EntityManager.HasComponent<EnemyId>(w));
            Assert.AreEqual(42, _world.EntityManager.GetComponentData<PlayerId>(w).Value);
            Assert.AreEqual(7, _world.EntityManager.GetComponentData<EnemyId>(w).Value);
            Assert.AreEqual(100, _world.EntityManager.GetComponentData<Health>(w).Current);
        }

        [Test]
        public void Identity_PartialMatch_OnlyOneMatches()
        {
            var sys = CreateSystemWithBoth();
            var em = _world.EntityManager;

            var x = em.CreateEntity();
            em.AddComponentData(x, new PlayerId { Value = 42 });
            em.AddComponentData(x, new EnemyId { Value = 7 });
            em.AddComponentData(x, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var w = em2.CreateEntity();
            em2.AddComponentData(w, new PlayerId { Value = 42 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);
            Assert.AreEqual(w, entities[0]);

            Assert.IsTrue(_world.EntityManager.HasComponent<EnemyId>(w));
            Assert.AreEqual(7, _world.EntityManager.GetComponentData<EnemyId>(w).Value);
        }

        [Test]
        public void Identity_Conflict1_DifferentWorldMatches_ReturnsValidationFailed()
        {
            var sys = CreateSystemWithBoth();
            var em = _world.EntityManager;

            var x = em.CreateEntity();
            em.AddComponentData(x, new PlayerId { Value = 42 });
            em.AddComponentData(x, new EnemyId { Value = 7 });
            em.AddComponentData(x, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var a = em2.CreateEntity();
            em2.AddComponentData(a, new PlayerId { Value = 42 });
            var b = em2.CreateEntity();
            em2.AddComponentData(b, new EnemyId { Value = 7 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, load.ErrorCode);
        }

        [Test]
        public void Identity_Conflict2_SameWorldEntityMatchedByMultiple_ReturnsValidationFailed()
        {
            var sys = CreateSystemWithBoth();
            var em = _world.EntityManager;

            var x = em.CreateEntity();
            em.AddComponentData(x, new PlayerId { Value = 42 });
            em.AddComponentData(x, new Health { Current = 100, Max = 200 });

            var y = em.CreateEntity();
            em.AddComponentData(y, new EnemyId { Value = 7 });
            em.AddComponentData(y, new Health { Current = 50, Max = 100 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var w = em2.CreateEntity();
            em2.AddComponentData(w, new PlayerId { Value = 42 });
            em2.AddComponentData(w, new EnemyId { Value = 7 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, load.ErrorCode);
        }

        [Test]
        public void Identity_WorldDuplicateKey_ReturnsValidationFailed()
        {
            var sys = CreateSystemWithPlayerOnly();
            var em = _world.EntityManager;

            var x = em.CreateEntity();
            em.AddComponentData(x, new PlayerId { Value = 42 });
            em.AddComponentData(x, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var a = em2.CreateEntity();
            em2.AddComponentData(a, new PlayerId { Value = 42 });
            var b = em2.CreateEntity();
            em2.AddComponentData(b, new PlayerId { Value = 42 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, load.ErrorCode);
        }

        [Test]
        public void Identity_SaveDuplicateKey_ReturnsValidationFailed()
        {
            var sys = CreateSystemWithPlayerOnly();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 42 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new PlayerId { Value = 42 });
            em.AddComponentData(b, new Health { Current = 200, Max = 300 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, load.ErrorCode);
        }

        [Test]
        public void Identity_MixedEntitiesWithAndWithoutKey()
        {
            var sys = CreateSystemWithPlayerOnly();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 42 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var b = em.CreateEntity();
            em.AddComponentData(b, new Health { Current = 500, Max = 600 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var w = em2.CreateEntity();
            em2.AddComponentData(w, new PlayerId { Value = 42 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = HealthQuery(_world.EntityManager);
            using var entities = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(2, entities.Length);

            int count100 = 0, count500 = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                var h = _world.EntityManager.GetComponentData<Health>(entities[i]);
                if (h.Current == 100) count100++;
                if (h.Current == 500) count500++;
            }
            Assert.AreEqual(1, count100);
            Assert.AreEqual(1, count500);

            Assert.IsTrue(_world.EntityManager.HasComponent<PlayerId>(w));
            Assert.AreEqual(100, _world.EntityManager.GetComponentData<Health>(w).Current);
        }

        [Test]
        public void Identity_EmptySaveData_Succeeds()
        {
            var sys = CreateSystemWithPlayerOnly();
            var em = _world.EntityManager;

            var x = em.CreateEntity();
            em.AddComponentData(x, new Health { Current = 100, Max = 200 });

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
        }
    }
}
