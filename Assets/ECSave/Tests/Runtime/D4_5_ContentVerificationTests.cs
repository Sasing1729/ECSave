using System;
using System.Collections.Generic;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Deserialize.Prephase;
using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class D4_5_ContentVerificationTests
    {
        private static SubSceneDefinition MakeDef(
            ContentVerificationMode mode,
            Func<IReadOnlyList<SceneLoadInfo>> provider)
        {
            return new SubSceneDefinition(
                true, false, false, false,
                mode,
                StreamingConsistencyMode.TimeBased,
                0, provider, null,
                null,
                Array.Empty<SubSceneDestroyedIdentityHandler>(),
                Array.Empty<PrefabMigrationStepBase>());
        }

        private static SubSceneStateSectionView MakeSection(
            NativeArray<byte> buffer, SceneLoadInfo[] scenes, out int written)
        {
            byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
            var w = new FormatWriter(p, buffer.Length);
            SubSceneStateSection.Write(ref w, scenes);

            bool hasHash = false;
            for (int i = 0; i < scenes.Length; i++)
                if (scenes[i].ContentHash != 0) { hasHash = true; break; }

            written = SubSceneStateLayout.ComputeBodySize(scenes.Length, hasHash);
            return SubSceneStateSection.Read(p, written);
        }

        private static SectionTable MakeTable(
            NativeArray<SectionEntry> entries, ulong offset, ulong length)
        {
            entries[0] = new SectionEntry
            {
                Kind = SectionKind.SubSceneState,
                Offset = offset,
                Length = length,
            };
            return new SectionTable
            {
                Entries = (SectionEntry*)NativeArrayUnsafeUtility.GetUnsafePtr(entries),
                Count = 1,
            };
        }

        private static bool HasMismatchWarning(WarningCollector warnings)
        {
            // WarningCollector には列挙 API がないため、内部の NativeList に
            // アクセスできない場合はバックドアが必要。ここではリフレクションで回避せず、
            // 現状の WarningCollector が Count を公開しているのみであることを前提に、
            // ToLoadWarnings 経由で検証する。
            var list = new List<LoadWarning>();
            warnings.ToLoadWarnings(list, null);
            foreach (var w in list)
                if (w.Kind == LoadWarningKind.SceneContentHashMismatch) return true;
            return false;
        }

        [Test]
        public void ModeNone_NoVerification()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var warnings = new WarningCollector(Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var saved = new[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAA),
                };
                var v = MakeSection(buffer, saved, out int written);
                var table = MakeTable(entries, 0, (ulong)written);
                var fileView = new ByteView(p, written);

                var def = MakeDef(ContentVerificationMode.None,
                    () => new[] { new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xBBBB) });

                var err = D4_5_SubSceneMatchPhase.Execute(def, table, fileView, warnings, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(HasMismatchWarning(warnings), Is.False);
            }
            finally { buffer.Dispose(); entries.Dispose(); warnings.Dispose(); }
        }

        [Test]
        public void ModeGuidOnly_NoHashVerification()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var warnings = new WarningCollector(Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var saved = new[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAA),
                };
                var v = MakeSection(buffer, saved, out int written);
                var table = MakeTable(entries, 0, (ulong)written);
                var fileView = new ByteView(p, written);

                var def = MakeDef(ContentVerificationMode.GuidOnly,
                    () => new[] { new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xBBBB) });

                var err = D4_5_SubSceneMatchPhase.Execute(def, table, fileView, warnings, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(HasMismatchWarning(warnings), Is.False,
                    "GuidOnly should not verify content hash");
            }
            finally { buffer.Dispose(); entries.Dispose(); warnings.Dispose(); }
        }

        [Test]
        public void ModeGuidAndContentHash_MatchingHash_NoWarning()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var warnings = new WarningCollector(Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var saved = new[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAA),
                };
                var v = MakeSection(buffer, saved, out int written);
                var table = MakeTable(entries, 0, (ulong)written);
                var fileView = new ByteView(p, written);

                var def = MakeDef(ContentVerificationMode.GuidAndContentHash,
                    () => new[] { new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAA) });

                var err = D4_5_SubSceneMatchPhase.Execute(def, table, fileView, warnings, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(HasMismatchWarning(warnings), Is.False);
            }
            finally { buffer.Dispose(); entries.Dispose(); warnings.Dispose(); }
        }

        [Test]
        public void ModeGuidAndContentHash_MismatchHash_EmitsWarning()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var warnings = new WarningCollector(Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var saved = new[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAA),
                };
                var v = MakeSection(buffer, saved, out int written);
                var table = MakeTable(entries, 0, (ulong)written);
                var fileView = new ByteView(p, written);

                var def = MakeDef(ContentVerificationMode.GuidAndContentHash,
                    () => new[] { new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xBBBB) });

                var err = D4_5_SubSceneMatchPhase.Execute(def, table, fileView, warnings, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None),
                    "Mismatch should emit warning, not error, and load should succeed");
                Assert.That(HasMismatchWarning(warnings), Is.True);
            }
            finally { buffer.Dispose(); entries.Dispose(); warnings.Dispose(); }
        }

        [Test]
        public void ModeGuidAndContentHash_SavedHashZero_NoWarning()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var warnings = new WarningCollector(Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var saved = new[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0UL),
                    new SceneLoadInfo(new SceneGuid(3, 4), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAAUL),
                };
                var v = MakeSection(buffer, saved, out int written);
                var table = MakeTable(entries, 0, (ulong)written);
                var fileView = new ByteView(p, written);

                var def = MakeDef(ContentVerificationMode.GuidAndContentHash,
                    () => new[]
                    {
                        new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xBBBB),
                        new SceneLoadInfo(new SceneGuid(3, 4), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xCCCC),
                    });

                var err = D4_5_SubSceneMatchPhase.Execute(def, table, fileView, warnings, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                var list = new List<LoadWarning>();
                warnings.ToLoadWarnings(list, null);
                int mismatches = 0;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Kind == LoadWarningKind.SceneContentHashMismatch) mismatches++;

                Assert.That(mismatches, Is.EqualTo(1),
                    "Only the scene with non-zero saved hash should be verified; " +
                    "GUID(3,4) has hash AAAA != CCCC → 1 mismatch; GUID(1,2) has hash 0 → skipped");
            }
            finally { buffer.Dispose(); entries.Dispose(); warnings.Dispose(); }
        }

        [Test]
        public void ModeGuidAndContentHash_NoProvider_NoWarning()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var warnings = new WarningCollector(Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var saved = new[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xAAAA),
                };
                var v = MakeSection(buffer, saved, out int written);
                var table = MakeTable(entries, 0, (ulong)written);
                var fileView = new ByteView(p, written);

                var def = MakeDef(ContentVerificationMode.GuidAndContentHash, provider: null);

                var err = D4_5_SubSceneMatchPhase.Execute(def, table, fileView, warnings, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(HasMismatchWarning(warnings), Is.False,
                    "Without provider, cannot verify; no warning emitted");
            }
            finally { buffer.Dispose(); entries.Dispose(); warnings.Dispose(); }
        }

        [Test]
        public void ModeGuidAndContentHash_SavedWithoutHash_NoWarning()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            var warnings = new WarningCollector(Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var saved = new[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0UL),
                };
                var v = MakeSection(buffer, saved, out int written);
                var table = MakeTable(entries, 0, (ulong)written);
                var fileView = new ByteView(p, written);

                var def = MakeDef(ContentVerificationMode.GuidAndContentHash,
                    () => new[] { new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xBBBB) });

                var err = D4_5_SubSceneMatchPhase.Execute(def, table, fileView, warnings, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(HasMismatchWarning(warnings), Is.False,
                    "Saved section has no hash → HasContentHash=false → verification skipped");
            }
            finally { buffer.Dispose(); entries.Dispose(); warnings.Dispose(); }
        }
    }
}
