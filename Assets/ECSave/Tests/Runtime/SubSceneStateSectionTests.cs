using NUnit.Framework;
using ECSave;
using ECSave.Internal.SubScene;
using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class SubSceneStateLayoutTests
    {
        [Test]
        public void Constants_HaveExpectedValues()
        {
            Assert.That(SubSceneStateLayout.HeaderSize, Is.EqualTo(8));
            Assert.That(SubSceneStateLayout.RecordSizeNoHash, Is.EqualTo(20));
            Assert.That(SubSceneStateLayout.RecordSizeWithHash, Is.EqualTo(28));
        }

        [TestCase(0, false, 8)]
        [TestCase(1, false, 28)]
        [TestCase(2, false, 48)]
        [TestCase(0, true, 8)]
        [TestCase(1, true, 36)]
        [TestCase(2, true, 64)]
        public void ComputeBodySize_ReturnsExpected(int count, bool hasHash, int expected)
        {
            Assert.That(SubSceneStateLayout.ComputeBodySize(count, hasHash), Is.EqualTo(expected));
        }
    }

    [TestFixture]
    public unsafe class SubSceneStateSectionTests
    {
        private static SceneLoadInfo[] MakeScenes(int count, bool withHash)
        {
            var arr = new SceneLoadInfo[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = new SceneLoadInfo(
                    new SceneGuid((ulong)(i + 1), (ulong)(i + 100)),
                    (SceneLoadState)(i % 3),
                    i % 2 == 0 ? SceneRestoreMode.Required : SceneRestoreMode.Optional,
                    withHash ? (ulong)(0x1000 + i * 0x10) : 0UL);
            }
            return arr;
        }

        [Test]
        public void WriteRead_Empty_Roundtrip()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 64);
                SubSceneStateSection.Write(ref w, new SceneLoadInfo[0]);

                var v = SubSceneStateSection.Read(p, 8);
                Assert.That(v.Count, Is.EqualTo(0u));
                Assert.That(v.HasContentHash, Is.False);
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_Null_RoundtripAsEmpty()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 64);
                SubSceneStateSection.Write(ref w, null);

                var v = SubSceneStateSection.Read(p, 8);
                Assert.That(v.Count, Is.EqualTo(0u));
                Assert.That(v.HasContentHash, Is.False);
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_NoHash_RecordSize20()
        {
            var scenes = MakeScenes(3, withHash: false);
            int size = SubSceneStateLayout.ComputeBodySize(3, hasContentHash: false);

            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 128);
                SubSceneStateSection.Write(ref w, scenes);

                var v = SubSceneStateSection.Read(p, size);
                Assert.That(v.Count, Is.EqualTo(3u));
                Assert.That(v.HasContentHash, Is.False);
                Assert.That(v.RecordSize, Is.EqualTo(20));

                for (int i = 0; i < 3; i++)
                {
                    var info = SubSceneStateSection.GetSceneLoadInfo(v, i);
                    Assert.That(info.Guid, Is.EqualTo(scenes[i].Guid));
                    Assert.That(info.State, Is.EqualTo(scenes[i].State));
                    Assert.That(info.RestoreMode, Is.EqualTo(scenes[i].RestoreMode));
                    Assert.That(info.ContentHash, Is.EqualTo(0UL));
                }
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_WithHash_RecordSize28()
        {
            var scenes = MakeScenes(3, withHash: true);
            int size = SubSceneStateLayout.ComputeBodySize(3, hasContentHash: true);

            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 128);
                SubSceneStateSection.Write(ref w, scenes);

                var v = SubSceneStateSection.Read(p, size);
                Assert.That(v.Count, Is.EqualTo(3u));
                Assert.That(v.HasContentHash, Is.True);
                Assert.That(v.RecordSize, Is.EqualTo(28));

                for (int i = 0; i < 3; i++)
                {
                    var info = SubSceneStateSection.GetSceneLoadInfo(v, i);
                    Assert.That(info.Guid, Is.EqualTo(scenes[i].Guid));
                    Assert.That(info.State, Is.EqualTo(scenes[i].State));
                    Assert.That(info.RestoreMode, Is.EqualTo(scenes[i].RestoreMode));
                    Assert.That(info.ContentHash, Is.EqualTo(scenes[i].ContentHash));
                }
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_MixedHashes_HasHashTrue()
        {
            var scenes = new[]
            {
                new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded),
                new SceneLoadInfo(new SceneGuid(3, 4), SceneLoadState.Loaded, SceneRestoreMode.Required, 0xABCDUL),
            };

            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 128);
                SubSceneStateSection.Write(ref w, scenes);

                int size = SubSceneStateLayout.ComputeBodySize(2, hasContentHash: true);
                var v = SubSceneStateSection.Read(p, size);
                Assert.That(v.HasContentHash, Is.True);
                Assert.That(SubSceneStateSection.GetContentHash(v, 0), Is.EqualTo(0UL));
                Assert.That(SubSceneStateSection.GetContentHash(v, 1), Is.EqualTo(0xABCDUL));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_NullPointer_Throws()
        {
            Assert.Throws<FormatException>(() => SubSceneStateSection.Read(null, 100));
        }

        [Test]
        public void Read_TooShort_Throws()
        {
            var buffer = new NativeArray<byte>(4, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                Assert.Throws<FormatException>(() => SubSceneStateSection.Read(p, 4));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_TruncatedBody_NoHash_Throws()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 256);
                SubSceneStateSection.Write(ref w, MakeScenes(3, withHash: false));

                Assert.Throws<FormatException>(() => SubSceneStateSection.Read(p, 28));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_TruncatedBody_WithHash_Throws()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 256);
                SubSceneStateSection.Write(ref w, MakeScenes(3, withHash: true));

                // Need 92 bytes (8 + 3*28), give only 36 (8 + 1*28)
                Assert.Throws<FormatException>(() => SubSceneStateSection.Read(p, 36));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_CountOverflow_Throws()
        {
            var buffer = new NativeArray<byte>(8, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                *(uint*)p = 1000000;
                *(uint*)(p + 4) = 0u;
                Assert.Throws<FormatException>(() => SubSceneStateSection.Read(p, 8));
            }
            finally { buffer.Dispose(); }
        }
    }
}
