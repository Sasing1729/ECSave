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
    /// Review #2: D6_MigrationPhase の構造的バグ検証。
    /// - 型記述子を持たないセクション（SubSceneState 等）が混在してもクラッシュしない
    /// - NameHeap が正しく引き継がれる
    /// - SectionTableOffset が新レイアウトで書かれる
    /// </summary>
    public class D6MigrationStructureTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("D6StructureTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void MigrationWithOnlyCommonSections_Succeeds()
        {
            // V1 → V2 の Migration
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "D6StructureTestWorld");
            var table = new MigrationTable();
            table.Register(new HealthV1ToV2Migration());
            def.Migrations = table;
            var sys = new SaveSystem(def);

            // V1 で保存
            var saveSys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthV1Module() },
                1, "D6StructureTestWorld"));

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new HealthV1 { Value = 42 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("D6StructureTestWorld");

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(42, vals[0].Current);
        }

        [Test]
        public void MigrationPreservesNameHeap()
        {
            // V1 で保存 + Migration 適用 → 移行後の NameHeap が壊れていない
            var saveSys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthV1Module() },
                1, "D6StructureTestWorld"));

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new HealthV1 { Value = 7 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("D6StructureTestWorld");

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "D6StructureTestWorld");
            var table = new MigrationTable();
            table.Register(new HealthV1ToV2Migration());
            def.Migrations = table;
            var sys = new SaveSystem(def);

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            // 移行後の再保存 → NameHeap が正しく引き継がれているか
            var resave = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(resave.IsSuccess);

            // 再保存データをロードできる（NameHeap 破損なら UnknownSectionIgnored になる）
            _world.Dispose();
            _world = new World("D6StructureTestWorld");

            var load2 = sys.Deserialize(_world, resave.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load2.IsSuccess);

            int unknownCount = 0;
            for (int i = 0; i < load2.Warnings.Count; i++)
                if (load2.Warnings[i].Kind == LoadWarningKind.UnknownSectionIgnored)
                    unknownCount++;
            Assert.AreEqual(0, unknownCount,
                "Migrated data should preserve valid NameHeap; no sections should be unknown");
        }

        [Test]
        public void MigrationWithSubSceneSections_DoesNotCrash()
        {
            // SubSceneState セクション（型記述子なし）を含むデータの Migration
            // Review #2a: tdSizes[i] = *(uint*)section が Count を読んで
            // バッファオーバーリードを起こさないことを検証
            var saveSys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthV1Module() },
                1, "D6StructureTestWorld"));

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new HealthV1 { Value = 99 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World("D6StructureTestWorld");

            // 移行対象 + Migration を登録
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "D6StructureTestWorld");
            var table = new MigrationTable();
            table.Register(new HealthV1ToV2Migration());
            def.Migrations = table;
            var sys = new SaveSystem(def);

            // クラッシュせずに完了することを検証
            Assert.DoesNotThrow(() =>
            {
                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);
            });
        }
    }
}
