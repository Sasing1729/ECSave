using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Deserialize.Prephase;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class D4_5_SubSceneMatchPhaseTests
    {
        private static SubSceneDefinition EnabledDef()
        {
            return new SubSceneDefinition(
                true, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                System.Array.Empty<PrefabMigrationStepBase>());
        }

        private static SubSceneDefinition DisabledDef()
        {
            return SubSceneDefinition.Empty;
        }

        private static SectionTable MakeTable(
            NativeArray<SectionEntry> entries,
            SectionKind kind,
            ulong offset,
            ulong length)
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

        [Test]
        public void DisabledDef_ReturnsNone()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var table = MakeTable(entries, SectionKind.SubSceneState, 0, 8);
                var view = new ByteView(ptr, 8);

                var err = D4_5_SubSceneMatchPhase.Execute(
                    DisabledDef(), table, view, out _, out bool has);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(has, Is.False);
            }
            finally { buffer.Dispose(); entries.Dispose(); }
        }

        [Test]
        public void NoSubSceneSection_ReturnsNone()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var table = MakeTable(entries, SectionKind.Component, 0, 8);
                var view = new ByteView(ptr, 8);

                var err = D4_5_SubSceneMatchPhase.Execute(
                    EnabledDef(), table, view, out _, out bool has);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(has, Is.False);
            }
            finally { buffer.Dispose(); entries.Dispose(); }
        }

        [Test]
        public void EmptyTable_ReturnsNone()
        {
            var buffer = new NativeArray<byte>(8, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var view = new ByteView(ptr, 8);
                var table = new SectionTable { Entries = null, Count = 0 };

                var err = D4_5_SubSceneMatchPhase.Execute(
                    EnabledDef(), table, view, out _, out bool has);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(has, Is.False);
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void ValidSection_Parsed()
        {
            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 128);
                var scenes = new SceneLoadInfo[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded),
                };
                SubSceneStateSection.Write(ref writer, scenes);

                // 修正: ComputeBodySize(1) → ComputeBodySize(scenes)
                int written = SubSceneStateLayout.ComputeBodySize(scenes);
                var table = MakeTable(entries, SectionKind.SubSceneState, 0, (ulong)written);
                var view = new ByteView(ptr, written);

                var err = D4_5_SubSceneMatchPhase.Execute(
                    EnabledDef(), table, view, out var parsed, out bool has);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(has, Is.True);
                Assert.That(parsed.Count, Is.EqualTo(1u));
            }
            finally { buffer.Dispose(); entries.Dispose(); }
        }

        [Test]
        public void OffsetBeyondView_ReturnsCorruptedData()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var table = MakeTable(entries, SectionKind.SubSceneState, 0, 200);
                var view = new ByteView(ptr, 64);

                var err = D4_5_SubSceneMatchPhase.Execute(
                    EnabledDef(), table, view, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { buffer.Dispose(); entries.Dispose(); }
        }

        [Test]
        public void TruncatedBody_ReturnsCorruptedData()
        {
            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 128);
                SubSceneStateSection.Write(ref writer, new SceneLoadInfo[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded),
                    new SceneLoadInfo(new SceneGuid(3, 4), SceneLoadState.Loaded),
                    new SceneLoadInfo(new SceneGuid(5, 6), SceneLoadState.Loaded),
                });

                // Claim length 12 (only 4 bytes of records available; need 60)
                var table = MakeTable(entries, SectionKind.SubSceneState, 0, 12);
                var view = new ByteView(ptr, 128);

                var err = D4_5_SubSceneMatchPhase.Execute(
                    EnabledDef(), table, view, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { buffer.Dispose(); entries.Dispose(); }
        }

        [Test]
        public void OffsetGreaterThanIntMax_ReturnsCorruptedData()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            var entries = new NativeArray<SectionEntry>(1, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var table = MakeTable(entries, SectionKind.SubSceneState, (ulong)int.MaxValue + 100, 8);
                var view = new ByteView(ptr, 64);

                var err = D4_5_SubSceneMatchPhase.Execute(
                    EnabledDef(), table, view, out _, out _);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.CorruptedData));
            }
            finally { buffer.Dispose(); entries.Dispose(); }
        }
    }
}
