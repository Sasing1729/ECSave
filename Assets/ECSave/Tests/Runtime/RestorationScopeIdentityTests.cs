using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// Review #7 / 設計書 9.2: RestorationScope.Custom が Identity 照合の範囲を制限する。
    /// </summary>
    public class RestorationScopeIdentityTests
    {
        private const string WorldName = "RestorationScopeIdentityWorld";
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World(WorldName);
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
                new IdentityModule[] { new PlayerIdModule() },
                1, WorldName);
            return new SaveSystem(def);
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        private static EntityQuery PlayerIdQuery(EntityManager em)
            => em.CreateEntityQuery(typeof(PlayerId));

        [Test]
        public void EntireWorld_InScopeMatching_UpdatesCorrectly()
        {
            // Baseline: EntireWorld での照合
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var a2 = em2.CreateEntity();
            em2.AddComponentData(a2, new PlayerId { Value = 1 });
            em2.AddComponentData(a2, new Health { Current = 10, Max = 20 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            Assert.AreEqual(100, em2.GetComponentData<Health>(a2).Current,
                "EntireWorld: existing entity should be matched and updated");
            using var all = em2.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(1, all.Length, "No new entity should be created");
        }

        [Test]
        public void CustomScope_InScopeEntity_Matched()
        {
            // Custom scope で範囲内の Entity は照合される
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;
            var a2 = em2.CreateEntity();
            em2.AddComponentData(a2, new PlayerId { Value = 1 });
            em2.AddComponentData(a2, new Health { Current = 10, Max = 20 });

            var scope = RestorationScope.Custom(PlayerIdQuery(em2));
            var load = sys.DeserializeAsync(_world, save.Data, scope, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            Assert.AreEqual(100, em2.GetComponentData<Health>(a2).Current,
                "In-scope entity should be matched and updated");
        }

        [Test]
        public void CustomScope_OutOfScopeEntity_NotMatched()
        {
            // Custom scope で範囲外の Entity は照合されず、
            // SaveData の値は新規 Entity として生成される
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            // A' は scope 外（PlayerOnlyFilter を持たない）、PlayerId=1 を持つ
            var a2 = em2.CreateEntity();
            em2.AddComponentData(a2, new PlayerId { Value = 1 });
            em2.AddComponentData(a2, new Health { Current = 10, Max = 20 });

            // B' は scope 内（PlayerOnlyFilter を持ち、PlayerId=99）
            var b2 = em2.CreateEntity();
            em2.AddComponentData(b2, new PlayerId { Value = 99 });
            em2.AddComponentData(b2, new Health { Current = 5, Max = 10 });
            em2.AddComponent<PlayerOnlyFilter>(b2);

            // scope = PlayerOnlyFilter を持つ Entity（= B' のみ）
            var scopeQuery = em2.CreateEntityQuery(typeof(PlayerOnlyFilter));
            var scope = RestorationScope.Custom(scopeQuery);

            var load = sys.DeserializeAsync(_world, save.Data, scope, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Load failed: {load.ErrorCode}");

            // A' は照合されず、元の値のまま
            Assert.AreEqual(10, em2.GetComponentData<Health>(a2).Current,
                "Out-of-scope entity should NOT be matched; original value preserved");

            // SaveData の PlayerId=1 は scope 内に該当なし → 新規 Entity が生成される
            using var all = em2.GetAllEntities(Allocator.Temp);
            int healthCount = 0;
            int pid1Count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (em2.HasComponent<Health>(e)) healthCount++;
                if (em2.HasComponent<PlayerId>(e) && em2.GetComponentData<PlayerId>(e).Value == 1)
                    pid1Count++;
            }
            Assert.GreaterOrEqual(healthCount, 2,
                "SaveData should have created a new entity because no in-scope match");
            Assert.AreEqual(2, pid1Count,
                "PlayerId=1 should now exist twice (original + new from SaveData)");
        }

        [Test]
        public void CustomScope_ConflictOutOfScope_NotDetected()
        {
            // Conflict は scope 内でのみ検出される。
            // scope 外に重複キーがあっても ValidationFailed にならない。
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            // A' : PlayerId=1, Health=10, PlayerOnlyFilter (scope 内)
            var a2 = em2.CreateEntity();
            em2.AddComponentData(a2, new PlayerId { Value = 1 });
            em2.AddComponentData(a2, new Health { Current = 10, Max = 20 });
            em2.AddComponent<PlayerOnlyFilter>(a2);

            // B' : PlayerId=1 (scope 外)
            var b2 = em2.CreateEntity();
            em2.AddComponentData(b2, new PlayerId { Value = 1 });

            // scope = PlayerOnlyFilter (A' のみ)
            var scopeQuery = em2.CreateEntityQuery(typeof(PlayerOnlyFilter));
            var scope = RestorationScope.Custom(scopeQuery);

            var load = sys.DeserializeAsync(_world, save.Data, scope, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess,
                "Conflict should be scope-limited; out-of-scope B' should not trigger ValidationFailed");
            Assert.AreEqual(100, em2.GetComponentData<Health>(a2).Current,
                "In-scope A' should be updated");
        }

        [Test]
        public void EntireWorld_ConflictInWorld_ReturnsValidationFailed()
        {
            // EntireWorld では scope 外の重複も検出される（既存挙動の回帰）
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var a = em.CreateEntity();
            em.AddComponentData(a, new PlayerId { Value = 1 });
            em.AddComponentData(a, new Health { Current = 100, Max = 200 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var em2 = _world.EntityManager;

            // A', B' 共に PlayerId=1（World 側に重複）
            var a2 = em2.CreateEntity();
            em2.AddComponentData(a2, new PlayerId { Value = 1 });
            em2.AddComponentData(a2, new Health { Current = 10, Max = 20 });
            em2.AddComponent<PlayerOnlyFilter>(a2);

            var b2 = em2.CreateEntity();
            em2.AddComponentData(b2, new PlayerId { Value = 1 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, load.ErrorCode,
                "EntireWorld should detect duplicate PlayerId in world");
        }
    }
}
