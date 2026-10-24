using System;
using System.Collections.Generic;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Deserialize.Prephase;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class D5_5_PrefabMigrationPhaseTests
    {
        private sealed class TestStep : PrefabMigrationStepBase
        {
            private readonly ulong _old;
            private readonly ulong _new;
            public override ulong OldPrefabSchemaHash => _old;
            public override ulong NewPrefabSchemaHash => _new;
            public override void Migrate(ref PrefabMigrationContext context) { }

            public TestStep(ulong oldHash, ulong newHash)
            {
                _old = oldHash;
                _new = newHash;
            }
        }

        [SetUp]
        public void SetUp()
        {
            PrefabSchemaRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PrefabSchemaRegistry.Clear();
        }

        private static SubSceneDefinition MakeDef(
            Func<Unity.Entities.Entity, PrefabIdentityInfo?> provider,
            params PrefabMigrationStepBase[] steps)
        {
            return new SubSceneDefinition(
                false, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                provider,
                Array.Empty<SubSceneDestroyedIdentityHandler>(),
                steps);
        }

        private static SectionTable MakeTable(
            NativeArray<SectionEntry> entries, SectionKind kind, ulong offset, ulong length)
        {
            entries[0] = new SectionEntry
            {
                Kind = kind,
                Offset = offset,
                Length = length,
            };
            return new SectionTable
            {
                Entries = (SectionEntry*)NativeArrayUnsafeUtility.GetUnsafePtr(entries),
                Count = 1,
            };
        }

        private static (PrephaseContext ctx, NativeArray<byte> buffer, NativeArray<SectionEntry> entries)
            MakeContextWithSection(SceneGuid dummy, params PrefabIdentityRecord[] records)
        {
            var ctx = PrephaseContext.Create(
                new SaveDefinition(Array.Empty<SaveTypeModule>(), 1, "W"), null);

            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);

            byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
            var w = new FormatWriter(p, 256);
            PrefabIdentitySection.Write(ref w, records);

            int size = PrefabIdentityLayout.ComputeBodySize(records.Length);
            ctx.View = new ByteView(p, 256);
            ctx.SectionTable = MakeTable(entries, SectionKind.PrefabIdentity, 0, (ulong)size);

            return (ctx, buffer, entries);
        }

        private static bool HasWarning(WarningCollector warnings, LoadWarningKind kind)
        {
            var list = new List<LoadWarning>();
            warnings.ToLoadWarnings(list, null);
            foreach (var w in list)
                if (w.Kind == kind) return true;
            return false;
        }

        private static int CountWarnings(WarningCollector warnings, LoadWarningKind kind)
        {
            var list = new List<LoadWarning>();
            warnings.ToLoadWarnings(list, null);
            int n = 0;
            foreach (var w in list)
                if (w.Kind == kind) n++;
            return n;
        }

        // === #1: def.HasPrefabIdentity == false ===
        [Test]
        public void DefWithoutPrefabIdentity_ReturnsNone()
        {
            var ctx = PrephaseContext.Create(
                new SaveDefinition(Array.Empty<SaveTypeModule>(), 1, "W"), null);
            try
            {
                // def は default (PrefabIdentityProvider == null)
                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out var parsed);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(parsed.HasValue, Is.False);
                Assert.That(ctx.PrefabMigrationsToApply, Is.Null);
            }
            finally { ctx.Dispose(); }
        }

        // === #3: セクション不在 ===
        [Test]
        public void NoSection_ReturnsNone()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var ctx = PrephaseContext.Create(
                new SaveDefinition(Array.Empty<SaveTypeModule>(), 1, "W"), null);
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                ctx.View = new ByteView(p, 64);
                ctx.SectionTable = MakeTable(entries, SectionKind.Component, 0, 8);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out var parsed);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(parsed.HasValue, Is.False);
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #4: Offset > int.MaxValue ===
        [Test]
        public void OffsetBeyondIntMax_ReturnsCorruptedData()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var ctx = PrephaseContext.Create(
                new SaveDefinition(Array.Empty<SaveTypeModule>(), 1, "W"), null);
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                ctx.View = new ByteView(p, 64);
                ctx.SectionTable = MakeTable(
                    entries, SectionKind.PrefabIdentity, (ulong)int.MaxValue + 100, 8);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #5: BoundsCheck 失敗 ===
        [Test]
        public void OffsetBeyondView_ReturnsCorruptedData()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var ctx = PrephaseContext.Create(
                new SaveDefinition(Array.Empty<SaveTypeModule>(), 1, "W"), null);
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                ctx.View = new ByteView(p, 64);
                ctx.SectionTable = MakeTable(entries, SectionKind.PrefabIdentity, 0, 200);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #6: Read で FormatException ===
        [Test]
        public void TruncatedBody_ReturnsCorruptedData()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0xBBBB),
                new PrefabIdentityRecord(2, 0xCCCC, 0xDDDD),
                new PrefabIdentityRecord(3, 0xEEEE, 0xFFFF));
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                // 3 records = 8 + 72 = 80 bytes; declare only 32
                entries[0] = new SectionEntry
                {
                    Kind = SectionKind.PrefabIdentity,
                    Offset = 0,
                    Length = 32,
                };
                ctx.SectionTable = new SectionTable
                {
                    Entries = (SectionEntry*)NativeArrayUnsafeUtility.GetUnsafePtr(entries),
                    Count = 1,
                };

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #7: PrefabSchemaRegistry 未登録 → skip silently ===
        [Test]
        public void RegistryMissing_SkipsSilently()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0xBBBB));
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                // PrefabSchemaRegistry に登録しない
                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out var parsed);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(parsed.HasValue, Is.True);
                Assert.That(ctx.PrefabMigrationsToApply, Is.Null);
                var list = new List<LoadWarning>();
                ctx.Warnings.ToLoadWarnings(list, null);
                Assert.That(list.Count, Is.EqualTo(0), "No warning when registry missing");
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #8: SchemaHash=0 / PrefabGuid=0 → skip ===
        [Test]
        public void ZeroGuidsAndHashes_Skipped()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0UL, 0xBBBB),
                new PrefabIdentityRecord(2, 0xAAAA, 0UL));
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                PrefabSchemaRegistry.Register(0xAAAA, 0xCCCC);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out var parsed);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(parsed.HasValue, Is.True);
                Assert.That(ctx.PrefabMigrationsToApply, Is.Null);
                var list = new List<LoadWarning>();
                ctx.Warnings.ToLoadWarnings(list, null);
                Assert.That(list.Count, Is.EqualTo(0));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #9: 現行と一致 → skip ===
        [Test]
        public void MatchingHash_NoWarningNoMigration()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0xBBBB));
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                PrefabSchemaRegistry.Register(0xAAAA, 0xBBBB);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(ctx.PrefabMigrationsToApply, Is.Null);
                var list = new List<LoadWarning>();
                ctx.Warnings.ToLoadWarnings(list, null);
                Assert.That(list.Count, Is.EqualTo(0));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #10: chain 解決失敗 ===
        [Test]
        public void HashMismatchNoChain_EmitsSceneContentHashMismatch()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0xBBBB));
            ctx.Definition.SubScene = MakeDef(_ => new PrefabIdentityInfo(1, 2));
            try
            {
                PrefabSchemaRegistry.Register(0xAAAA, 0xCCCC);
                // No migration steps registered

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(ctx.PrefabMigrationsToApply, Is.Null);
                Assert.That(HasWarning(ctx.Warnings, LoadWarningKind.SceneContentHashMismatch), Is.True);
                Assert.That(HasWarning(ctx.Warnings, LoadWarningKind.PrefabMigrated), Is.False);
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #11: chain 解決成功 ===
        [Test]
        public void HashMismatchWithChain_EmitsPrefabMigrated()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0xBBBB));
            ctx.Definition.SubScene = MakeDef(
                _ => new PrefabIdentityInfo(1, 2),
                new TestStep(0xBBBB, 0xCCCC));
            try
            {
                PrefabSchemaRegistry.Register(0xAAAA, 0xCCCC);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(ctx.PrefabMigrationsToApply, Is.Not.Null);
                Assert.That(ctx.PrefabMigrationsToApply.Count, Is.EqualTo(1));
                Assert.That(ctx.PrefabMigrationsToApply[0].PersistentId, Is.EqualTo(1u));
                Assert.That(ctx.PrefabMigrationsToApply[0].Steps.Length, Is.EqualTo(1));
                Assert.That(HasWarning(ctx.Warnings, LoadWarningKind.PrefabMigrated), Is.True);
                Assert.That(HasWarning(ctx.Warnings, LoadWarningKind.SceneContentHashMismatch), Is.False);
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === #12: 同一 prefabGuid を複数 Entity ===
        [Test]
        public void MultipleEntitiesSameGuid_EachGetsEntry()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0xBBBB),
                new PrefabIdentityRecord(2, 0xAAAA, 0xBBBB),
                new PrefabIdentityRecord(3, 0xAAAA, 0xBBBB));
            ctx.Definition.SubScene = MakeDef(
                _ => new PrefabIdentityInfo(1, 2),
                new TestStep(0xBBBB, 0xCCCC));
            try
            {
                PrefabSchemaRegistry.Register(0xAAAA, 0xCCCC);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(ctx.PrefabMigrationsToApply.Count, Is.EqualTo(3));
                Assert.That(CountWarnings(ctx.Warnings, LoadWarningKind.PrefabMigrated), Is.EqualTo(3));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === 混合: 一致 / 不一致+chain / 不一致-chain ===
        [Test]
        public void MixedEntities_EachHandledIndependently()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0xBBBB),  // matches
                new PrefabIdentityRecord(2, 0xCCCC, 0xDDDD),  // mismatch + chain
                new PrefabIdentityRecord(3, 0xEEEE, 0xFFFF)); // mismatch no chain
            ctx.Definition.SubScene = MakeDef(
                _ => new PrefabIdentityInfo(1, 2),
                new TestStep(0xDDDD, 0x1111));
            try
            {
                PrefabSchemaRegistry.Register(0xAAAA, 0xBBBB);
                PrefabSchemaRegistry.Register(0xCCCC, 0x1111);
                PrefabSchemaRegistry.Register(0xEEEE, 0x2222);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(ctx.PrefabMigrationsToApply.Count, Is.EqualTo(1));
                Assert.That(ctx.PrefabMigrationsToApply[0].PersistentId, Is.EqualTo(2u));
                Assert.That(CountWarnings(ctx.Warnings, LoadWarningKind.PrefabMigrated), Is.EqualTo(1));
                Assert.That(CountWarnings(ctx.Warnings, LoadWarningKind.SceneContentHashMismatch), Is.EqualTo(1));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }

        // === 複数ステップ連鎖 ===
        [Test]
        public void MultiStepChain_ResolvedAndQueued()
        {
            var (ctx, buffer, entries) = MakeContextWithSection(default,
                new PrefabIdentityRecord(1, 0xAAAA, 0x1000));
            ctx.Definition.SubScene = MakeDef(
                _ => new PrefabIdentityInfo(1, 2),
                new TestStep(0x1000, 0x2000),
                new TestStep(0x2000, 0x3000),
                new TestStep(0x3000, 0x4000));
            try
            {
                PrefabSchemaRegistry.Register(0xAAAA, 0x4000);

                var err = D5_5_PrefabMigrationPhase.Execute(ref ctx, out _);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(ctx.PrefabMigrationsToApply.Count, Is.EqualTo(1));
                Assert.That(ctx.PrefabMigrationsToApply[0].Steps.Length, Is.EqualTo(3));
            }
            finally { ctx.Dispose(); buffer.Dispose(); entries.Dispose(); }
        }
    }
}
