using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class MigrationTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("MigrationTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("MigrationTestWorld");
        }

        private SaveSystem CreateV1System()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthV1Module() },
                1, "MigrationTestWorld");
            return new SaveSystem(def);
        }

        private SaveSystem CreateV2SystemWithMigration()
        {
            var table = new MigrationTable();
            table.Register(new HealthV1ToV2Migration());

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "MigrationTestWorld");
            def.Migrations = table;
            return new SaveSystem(def);
        }

        private SaveSystem CreateV2SystemWithoutMigration()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "MigrationTestWorld");
            return new SaveSystem(def);
        }

        [Test]
        public void Migration_HealthV1ToV2_AppliesCorrectly()
        {
            var sys1 = CreateV1System();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new HealthV1 { Value = 42 });

            var save = sys1.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();

            var sys2 = CreateV2SystemWithMigration();
            var load = sys2.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(1, chunks.Length);

            var handle = _world.EntityManager.GetComponentTypeHandle<Health>(isReadOnly: true);
            var vals = chunks[0].GetNativeArray(ref handle);
            Assert.Greater(vals.Length, 0);
            Assert.AreEqual(42, vals[0].Current);
            Assert.AreEqual(0, vals[0].Max);
        }

        [Test]
        public void Migration_NoMigrationRegistered_SchemaMismatch()
        {
            var sys1 = CreateV1System();
            var em = _world.EntityManager;

            var e = em.CreateEntity();
            em.AddComponentData(e, new HealthV1 { Value = 42 });

            var save = sys1.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var sys2 = CreateV2SystemWithoutMigration();
            var load = sys2.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.SchemaMismatch, load.ErrorCode);
        }

        [Test]
        public void Migration_MultipleEntities_Migrated()
        {
            var sys1 = CreateV1System();
            var em = _world.EntityManager;

            for (int i = 0; i < 5; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new HealthV1 { Value = i * 10 });
            }

            var save = sys1.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var sys2 = CreateV2SystemWithMigration();
            var load = sys2.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(5, vals.Length);

            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < vals.Length; i++)
                seen.Add(vals[i].Current);

            for (int i = 0; i < 5; i++)
                Assert.IsTrue(seen.Contains(i * 10), $"missing {i * 10}");

            for (int i = 0; i < vals.Length; i++)
                Assert.AreEqual(0, vals[i].Max);
        }

        [Test]
        public void Migration_NoMismatch_NoMigrationApplied()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "MigrationTestWorld");
            var table = new MigrationTable();
            def.Migrations = table;
            var sys = new SaveSystem(def);

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 100, Max = 200 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(100, vals[0].Current);
            Assert.AreEqual(200, vals[0].Max);
        }
    }
}
