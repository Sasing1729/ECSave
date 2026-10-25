using System;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests.Runtime
{
    internal sealed class OldNameHealthModule : SaveTypeModulePlain<Health>
    {
        private readonly HealthModule _inner = new HealthModule();

        public override ulong SchemaHash => _inner.SchemaHash;
        public override string FullTypeName => RenamedFromTests.OldName;
        public override byte[] SchemaHashInput => _inner.SchemaHashInput;
        public override string[] FieldNames => _inner.FieldNames;

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
            => throw new NotSupportedException();
    }

    internal sealed class OldNameHealthV1Module : SaveTypeModulePlain<HealthV1>
    {
        public override ulong SchemaHash => HealthV1Module.Hash;
        public override string FullTypeName => RenamedFromTests.OldName;
        public override byte[] SchemaHashInput => HealthV1Module.Input;
        public override string[] FieldNames => HealthV1Module.Names;

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
            => throw new NotSupportedException();
    }

    public sealed class RenamedHealthScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<Health>().RenamedFrom("Game.Domain.OldHealth");
            b.WorldId("RenameWorld");
        }
    }

    public class RenamedFromTests
    {
        public const string OldName = "Game.Domain.OldHealth";
        private const string NewName = "Game.Domain.Health";
        private const string WorldName = "RenameWorld";

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

        private static ulong H(string s) => SchemaHashCore.HashUtf8(s);

        private void Restart()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        private byte[] SaveWith(SaveTypeModule[] modules, Action<EntityManager> populate)
        {
            var sys = new SaveSystem(new SaveDefinition(modules, 1, WorldName));
            populate(_world.EntityManager);
            var r = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(r.IsSuccess, $"Serialize failed: {r.ErrorCode}");
            return r.Data;
        }

        private static SaveSystem NewSystem(Action<MigrationTable> configure, params SaveTypeModule[] modules)
        {
            var def = new SaveDefinition(modules, 1, WorldName);
            var table = new MigrationTable();
            configure?.Invoke(table);
            def.Migrations = table;
            return new SaveSystem(def);
        }

        private LoadResult Load(SaveSystem sys, byte[] data)
            => sys.Deserialize(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

        private static void AddHealth(EntityManager em, int current, int max)
        {
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = current, Max = max });
        }

        [Test]
        public void RenamedSection_LoadsIntoCurrentModule()
        {
            var data = SaveWith(new SaveTypeModule[] { new OldNameHealthModule() },
                em => AddHealth(em, 42, 100));
            Restart();

            var sys = NewSystem(t => t.RegisterRenamed(H(OldName), H(NewName)), new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(1, load.EntityCount);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(42, vals[0].Current);
            Assert.AreEqual(100, vals[0].Max);
        }

        [Test]
        public void RenamedSection_WithoutMapping_IsIgnoredWithWarning()
        {
            var data = SaveWith(new SaveTypeModule[] { new OldNameHealthModule() },
                em => AddHealth(em, 42, 100));
            Restart();

            var sys = NewSystem(null, new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            bool warned = false;
            for (int i = 0; i < load.Warnings.Count; i++)
                if (load.Warnings[i].Kind == LoadWarningKind.UnknownSectionIgnored) warned = true;
            Assert.IsTrue(warned);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            Assert.AreEqual(0, q.CalculateEntityCount());
        }

        [Test]
        public void RenamedSection_WithSchemaChange_MigratesThroughNewName()
        {
            var data = SaveWith(new SaveTypeModule[] { new OldNameHealthV1Module() }, em =>
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new HealthV1 { Value = 42 });
            });
            Restart();

            var sys = NewSystem(t =>
            {
                t.RegisterRenamed(H(OldName), H(NewName));
                t.Register(new HealthV1ToV2Migration());
            }, new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(42, vals[0].Current);
            Assert.AreEqual(0, vals[0].Max);
        }

        [Test]
        public void MigratedData_ReloadsWithoutRenamedFrom()
        {
            // 1. 旧名 + 旧レイアウト (HealthV1) で保存
            var data = SaveWith(new SaveTypeModule[] { new OldNameHealthV1Module() }, em =>
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new HealthV1 { Value = 42 });
            });
            Restart();

            // 2. RenamedFrom + Migration でロード（移行）
            var sysMigrated = NewSystem(t =>
            {
                t.RegisterRenamed(H(OldName), H(NewName));
                t.Register(new HealthV1ToV2Migration());
            }, new HealthModule());

            var load1 = Load(sysMigrated, data);
            Assert.IsTrue(load1.IsSuccess, $"Migration load failed: {load1.ErrorCode}");

            var q1 = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using (var vals1 = q1.ToComponentDataArray<Health>(Allocator.Temp))
            {
                Assert.AreEqual(1, vals1.Length);
                Assert.AreEqual(42, vals1[0].Current);
            }

            // 3. 移行後のデータを再保存
            var saveMigrated = sysMigrated.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(saveMigrated.IsSuccess, $"Re-save failed: {saveMigrated.ErrorCode}");

            // 4. RenamedFrom 未登録の SaveDefinition で再ロード
            //    D6_MigrationPhase の NameHash 更新により、移行後のデータは
            //    新名で書かれているはずなので、RenamedFrom なしでも解決できる。
            Restart();
            var sysPlain = NewSystem(null, new HealthModule());
            var load2 = Load(sysPlain, saveMigrated.Data);

            Assert.IsTrue(load2.IsSuccess, $"Plain reload failed: {load2.ErrorCode}");

            bool hasUnknownWarning = false;
            for (int i = 0; i < load2.Warnings.Count; i++)
            {
                if (load2.Warnings[i].Kind == LoadWarningKind.UnknownSectionIgnored)
                    hasUnknownWarning = true;
            }
            Assert.IsFalse(hasUnknownWarning,
                "Migrated data should have NameHash updated to the new name; " +
                "reloading without RenamedFrom should not emit UnknownSectionIgnored");

            var q2 = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using (var vals2 = q2.ToComponentDataArray<Health>(Allocator.Temp))
            {
                Assert.AreEqual(1, vals2.Length);
                Assert.AreEqual(42, vals2[0].Current);
                Assert.AreEqual(0, vals2[0].Max);
            }
        }

        [Test]
        public void MigratedData_ReMigratedFromOldLayout_StillWorks()
        {
            // より踏み込んだ検証：旧名 + 旧レイアウト → RenamedFrom + Migration →
            // さらに「旧名 + 旧レイアウト」から再ロードできること。
            // これは移行後のデータの NameHash が新名になり、かつ SchemaHash が
            // 新レイアウトのハッシュになっていることの確認。
            var dataOld = SaveWith(new SaveTypeModule[] { new OldNameHealthV1Module() }, em =>
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new HealthV1 { Value = 7 });
            });
            Restart();

            var sysMigrated = NewSystem(t =>
            {
                t.RegisterRenamed(H(OldName), H(NewName));
                t.Register(new HealthV1ToV2Migration());
            }, new HealthModule());

            var load1 = Load(sysMigrated, dataOld);
            Assert.IsTrue(load1.IsSuccess, $"Migration load failed: {load1.ErrorCode}");

            var resaved = sysMigrated.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(resaved.IsSuccess);

            // 再保存データは「新名 + 新レイアウト」になっている。
            // HealthV1ToV2Migration は旧 V1 から新 V2 への変換だが、
            // 新名で保存されている以上、追加の Migration は不要。
            Restart();
            var sysPlain = NewSystem(null, new HealthModule());
            var load2 = Load(sysPlain, resaved.Data);
            Assert.IsTrue(load2.IsSuccess);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(7, vals[0].Current);
            Assert.AreEqual(0, vals[0].Max);
        }

        [Test]
        public void OldAndNewSectionsBothPresent_ReturnsSchemaMismatch()
        {
            var data = SaveWith(
                new SaveTypeModule[] { new OldNameHealthModule(), new HealthModule() },
                em => AddHealth(em, 1, 2));
            Restart();

            var sys = NewSystem(t => t.RegisterRenamed(H(OldName), H(NewName)), new HealthModule());
            var load = Load(sys, data);

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.SchemaMismatch, load.ErrorCode);
        }

        [Test]
        public void CurrentName_TakesPriorityOverRename()
        {
            var data = SaveWith(new SaveTypeModule[] { new HealthModule() },
                em => AddHealth(em, 7, 8));
            Restart();

            var sys = NewSystem(t => t.RegisterRenamed(H(NewName), H(OldName)), new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(7, vals[0].Current);
        }

        [Test]
        public void Scope_RenamedFrom_IsRegisteredAndLoadsOldName()
        {
            var scope = ScriptableObject.CreateInstance<RenamedHealthScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var table = sys.Definition.Migrations;

                Assert.IsNotNull(table);
                Assert.AreEqual(1, table.RenamedCount);
                Assert.IsTrue(table.TryResolveRenamed(H(OldName), out var target));
                Assert.AreEqual(H(NewName), target);

                var data = SaveWith(new SaveTypeModule[] { new OldNameHealthModule() },
                    em => AddHealth(em, 42, 100));
                Restart();

                var load = Load(sys, data);
                Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
                using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
                Assert.AreEqual(1, vals.Length);
                Assert.AreEqual(42, vals[0].Current);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }
        }

        [Test]
        public void Builder_RenamedFromSameAsCurrentName_Throws()
        {
            var impl = new SaveDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(
                () => impl.RegisterComponent<Health>().RenamedFrom(NewName));
            Assert.AreEqual(RegistrationErrorCodes.RenamedFromSameAsCurrent, ex.Code);
        }

        [Test]
        public void Builder_RenamedFromSameAsCurrentNameWithPlus_StillThrows()
        {
            // B-8: oldName の '+' → '.' 正規化により、リフレクション形式の
            // "Game.Domain+Health" も現在名と等しいと判定される。
            var impl = new SaveDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(
                () => impl.RegisterComponent<Health>().RenamedFrom("Game.Domain+Health"));
            Assert.AreEqual(RegistrationErrorCodes.RenamedFromSameAsCurrent, ex.Code);
        }

        [Test]
        public void Builder_SameOldNameForDifferentTypes_Throws()
        {
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old.Shared");
            var ex = Assert.Throws<SaveRegistrationException>(
                () => impl.RegisterComponent<HealthV1>().RenamedFrom("Old.Shared"));
            Assert.AreEqual(RegistrationErrorCodes.RenamedFromConflict, ex.Code);
        }

        [Test]
        public void Builder_SameOldNameDifferentFormat_Collides()
        {
            // B-8: "Old+Shared" と "Old.Shared" は正規化後に同一視される。
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old+Shared");
            var ex = Assert.Throws<SaveRegistrationException>(
                () => impl.RegisterComponent<HealthV1>().RenamedFrom("Old.Shared"));
            Assert.AreEqual(RegistrationErrorCodes.RenamedFromConflict, ex.Code);
        }

        [Test]
        public void Builder_SamePairTwice_IsIdempotent()
        {
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old.A").RenamedFrom("Old.A");

            var def = impl.Build();
            Assert.IsNotNull(def.Migrations);
            Assert.AreEqual(1, def.Migrations.RenamedCount);
        }

        [Test]
        public void Builder_SamePairMixedFormat_IsIdempotent()
        {
            // B-8: "Old+A" と "Old.A" は正規化後に同一ペアと判定され、
            // 二重登録例外（ECSAVER007）にならない。
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old+A").RenamedFrom("Old.A");

            var def = impl.Build();
            Assert.AreEqual(1, def.Migrations.RenamedCount);
        }

        [Test]
        public void Builder_ChainedRenames_AllRegistered()
        {
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old.A").RenamedFrom("Old.B");

            var def = impl.Build();
            Assert.AreEqual(2, def.Migrations.RenamedCount);
            Assert.IsTrue(def.Migrations.TryResolveRenamed(H("Old.A"), out var a));
            Assert.IsTrue(def.Migrations.TryResolveRenamed(H("Old.B"), out var b));
            Assert.AreEqual(H(NewName), a);
            Assert.AreEqual(H(NewName), b);
        }

        [Test]
        public void Builder_RenamedFromWithPlus_IsNormalizedToDot()
        {
            // B-8 の直接検証：リフレクション形式 "A.B+Inner" を
            // Roslyn 形式 "A.B.Inner" に正規化して登録する。
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Game.Domain+OldHealth");

            var def = impl.Build();
            Assert.IsNotNull(def.Migrations);

            // 正規化された '.' 形式で解決できる
            Assert.IsTrue(def.Migrations.TryResolveRenamed(
                H("Game.Domain.OldHealth"), out var target));
            Assert.AreEqual(H(NewName), target);

            // '+' 形式のままでは解決できない（正規化により '.' に置換されているため）
            Assert.IsFalse(def.Migrations.TryResolveRenamed(
                H("Game.Domain+OldHealth"), out _),
                "'+' must be normalized to '.' before hashing; " +
                "raw '+' form should not be reachable");
        }
    }
}
