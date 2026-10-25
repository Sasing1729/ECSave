using System;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// Review #8a / #9 / #10 の回帰テスト。
    /// </summary>
    public class PhaseCRegressionTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("PhaseCWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("PhaseCWorld");
        }

        // ══════════════════════════════════════════════════════════════
        // #8a: RegistrationId 採番（直接コンストラクタ経由）
        // ══════════════════════════════════════════════════════════════

        [Test]
        public void DirectConstructor_AssignsRegistrationIds_UniqueForModules()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[]
                {
                    new HealthModule(),
                    new StunnedModule(),
                    new InventoryElementModule(),
                },
                1, "PhaseCWorld");

            Assert.AreEqual(0, def.Modules[0].RegistrationId);
            Assert.AreEqual(1, def.Modules[1].RegistrationId);
            Assert.AreEqual(2, def.Modules[2].RegistrationId);
        }

        [Test]
        public void DirectConstructor_AssignsRegistrationIds_UniqueForIdentities()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                new IdentityModule[]
                {
                    new PlayerIdModule(),
                    new EnemyIdModule(),
                },
                1, "PhaseCWorld");

            Assert.AreEqual(0, def.Identities[0].RegistrationId);
            Assert.AreEqual(1, def.Identities[1].RegistrationId);
        }

        [Test]
        public void BuilderConstructor_AlsoAssignsIds()
        {
            // SaveDefinitionBuilderImpl 経由で既に採番済み。上書きされても同じ値。
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterModule(new HealthModule());
            impl.RegisterModule(new StunnedModule());

            var def = impl.Build();
            Assert.AreEqual(0, def.Modules[0].RegistrationId);
            Assert.AreEqual(1, def.Modules[1].RegistrationId);
        }

        [Test]
        public void MultiModuleStructuralChange_DoesNotDropComponents()
        {
            // Review #8a の実害シナリオ:
            //   - 複数モジュール (Health, Stunned, InventoryElement) を直接コンストラクタで生成
            //   - P4_StructuralChangePhase の CollectCoveredPids が moduleIndex で
            //     正しく被覆判定できるか検証
            var def = new SaveDefinition(
                new SaveTypeModule[]
                {
                    new HealthModule(),
                    new StunnedModule(),
                    new InventoryElementModule(),
                },
                1, "PhaseCWorld");
            var sys = new SaveSystem(def);

            // 保存: e1 は Health のみ、e2 は Stunned + Inventory を持つ
            var em = _world.EntityManager;
            var e1 = em.CreateEntity();
            em.AddComponentData(e1, new Health { Current = 10, Max = 20 });

            var e2 = em.CreateEntity();
            em.AddComponentData(e2, new Stunned { FramesRemaining = 5 });
            var buf = em.AddBuffer<InventoryElement>(e2);
            buf.Add(new InventoryElement { ItemId = 1, Count = 2 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

            // ロード: ByRegistration で、保存データに無いコンポーネントを除去
            // （修正前は RegistrationId 衝突で P4 が誤動作していた）
            RestartWorld();
            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

            // e1 は Health を保持、e2 は Stunned と Inventory を保持
            var qHealth = _world.EntityManager.CreateEntityQuery(typeof(Health));
            Assert.AreEqual(1, qHealth.CalculateEntityCount());

            var qStunned = _world.EntityManager.CreateEntityQuery(typeof(Stunned));
            Assert.AreEqual(1, qStunned.CalculateEntityCount());

            var qInv = _world.EntityManager.CreateEntityQuery(typeof(InventoryElement));
            Assert.AreEqual(1, qInv.CalculateEntityCount());
        }

        // ══════════════════════════════════════════════════════════════
        // #9: Disabled タグ付き Entity の保存
        // ══════════════════════════════════════════════════════════════

        [Test]
        public void DisabledEntity_IsSerialized()
        {
            var sys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "PhaseCWorld"));

            var em = _world.EntityManager;
            var enabled = em.CreateEntity();
            em.AddComponentData(enabled, new Health { Current = 10, Max = 20 });

            var disabled = em.CreateEntity();
            em.AddComponentData(disabled, new Health { Current = 99, Max = 100 });
            em.AddComponent<Disabled>(disabled);

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

            RestartWorld();

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(2, vals.Length,
                "Both enabled and disabled entities should be restored");

            int enabledCount = 0, disabledCount = 0;
            for (int i = 0; i < vals.Length; i++)
            {
                if (vals[i].Current == 10) enabledCount++;
                if (vals[i].Current == 99) disabledCount++;
            }
            Assert.AreEqual(1, enabledCount);
            Assert.AreEqual(1, disabledCount);
        }

        [Test]
        public void DisabledEntity_ByScope_NotDeletedAfterRoundTrip()
        {
            // Review #9 のデータ消失シナリオ:
            //   ByScope ロードで、保存に含まれていれば削除されないこと
            var sys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "PhaseCWorld"));

            var em = _world.EntityManager;
            var disabled = em.CreateEntity();
            em.AddComponentData(disabled, new Health { Current = 42, Max = 100 });
            em.AddComponent<Disabled>(disabled);

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            Assert.AreEqual(1, q.CalculateEntityCount(),
                "Disabled entity stored in save data should not be deleted by ByScope");
        }

        [Test]
        public void DisabledTag_Extra_ByScope_StillDeleted()
        {
            // 保存に含まれない Disabled は削除される（意図通り）
            var sys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                1, "PhaseCWorld"));

            // 空の SaveData
            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra, new Health { Current = 1, Max = 1 });
            _world.EntityManager.AddComponent<Disabled>(extra);

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            using var all = _world.EntityManager.GetAllEntities(Allocator.Temp);
            Assert.AreEqual(0, all.Length,
                "Disabled entity not in save data should be deleted by ByScope");
        }

        // ══════════════════════════════════════════════════════════════
        // #10: AddSelections 順序
        // ══════════════════════════════════════════════════════════════

        [Test]
        public void SelectionAddedEntity_IsRecognizedByChunkComponent()
        {
            // Review #10: Selection で追加された Entity が ChunkComponent の
            // メンバーとして認識されることを検証。
            //
            // シナリオ:
            //   - 通常コンポーネントを持たない bare Entity を Selection が選択
            //   - その Entity に ChunkComponent が付いている
            //   - ロード後、bare Entity が復元され、ChunkComponent も復元される
            var def = new SaveDefinition(
                new SaveTypeModule[] { new ChunkDataModule() },
                System.Array.Empty<IdentityModule>(),
                new SelectionModule[] { new SelectingChunkDataModule() },
                1, "PhaseCWorld");
            var sys = new SaveSystem(def);

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddChunkComponentData<ChunkData>(e);
            var chunk = em.GetChunk(e);
            em.SetChunkComponentData(chunk, new ChunkData { Value = 42 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

            // 保存データに ChunkData セクションが含まれていること
            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);

            bool hasChunkSection = false;
            unsafe
            {
                fixed (byte* p = raw)
                {
                    var reader = new FormatReader(new ByteView(p, raw.Length));
                    reader.ReadHeader();
                    var table = reader.ReadSectionTable();
                    for (int i = 0; i < table.Count; i++)
                        if (table.Entries[i].Kind == SectionKind.ChunkComponent)
                            hasChunkSection = true;
                }
            }
            Assert.IsTrue(hasChunkSection,
                "ChunkComponent section should be present when Selection " +
                "adds an entity with ChunkComponent (Review #10)");
        }

        private sealed class SelectingChunkDataModule : SelectionModule
        {
            public override string FullTypeName => typeof(ChunkData).FullName;
            public override string FieldPath => "Self";

            public override EntityQuery BuildQuery(EntityManager em)
                => em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ChunkComponentReadOnly<ChunkData>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities,
                });

            public override void ExtractSelected(
                EntityQuery query, EntityManager em,
                NativeParallelHashSet<Entity> selected)
            {
                using var entities = query.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                    selected.Add(entities[i]);
            }
        }
    }
}
