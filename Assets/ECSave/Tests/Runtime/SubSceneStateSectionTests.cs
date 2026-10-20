using NUnit.Framework;
using ECSave;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using FormatException = ECSave.Format.FormatException;
using ECSave.Format;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class SubSceneStateLayoutTests
    {
        [Test]
        public void Constants_HaveExpectedValues()
        {
            Assert.That(SubSceneStateLayout.HeaderSize, Is.EqualTo(8));
            Assert.That(SubSceneStateLayout.RecordSize, Is.EqualTo(20));
            Assert.That(SubSceneStateLayout.GuidHiOffset, Is.EqualTo(0));
            Assert.That(SubSceneStateLayout.GuidLoOffset, Is.EqualTo(8));
            Assert.That(SubSceneStateLayout.StateOffset, Is.EqualTo(16));
            Assert.That(SubSceneStateLayout.ModeOffset, Is.EqualTo(17));
        }

        [TestCase(0, 8)]
        [TestCase(1, 28)]
        [TestCase(2, 48)]
        [TestCase(10, 208)]
        public void ComputeBodySize_ReturnsExpected(int count, int expected)
        {
            Assert.That(SubSceneStateLayout.ComputeBodySize(count), Is.EqualTo(expected));
        }
    }

    [TestFixture]
    public unsafe class SubSceneStateSectionTests
    {
        private static SceneLoadInfo[] MakeScenes(int count)
        {
            var arr = new SceneLoadInfo[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = new SceneLoadInfo(
                    new SceneGuid((ulong)(i + 1), (ulong)(i + 100)),
                    (SceneLoadState)(i % 3),
                    i % 2 == 0 ? SceneRestoreMode.Required : SceneRestoreMode.Optional);
            }
            return arr;
        }

        [Test]
        public void WriteRead_Empty_Roundtrip()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 64);
                SubSceneStateSection.Write(ref writer, new SceneLoadInfo[0]);

                var view = SubSceneStateSection.Read(ptr, 8);
                Assert.That(view.Count, Is.EqualTo(0u));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_Null_RoundtripAsEmpty()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 64);
                SubSceneStateSection.Write(ref writer, null);

                var view = SubSceneStateSection.Read(ptr, 8);
                Assert.That(view.Count, Is.EqualTo(0u));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_SingleScene_AllFieldsPreserved()
        {
            var scenes = new SceneLoadInfo[]
            {
                new SceneLoadInfo(new SceneGuid(0xDEAD, 0xBEEF),
                    SceneLoadState.Loaded, SceneRestoreMode.Optional),
            };

            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 128);
                SubSceneStateSection.Write(ref writer, scenes);

                var view = SubSceneStateSection.Read(ptr, 28);
                Assert.That(view.Count, Is.EqualTo(1u));

                var info = SubSceneStateSection.GetSceneLoadInfo(view, 0);
                Assert.That(info.Guid.Hi, Is.EqualTo(0xDEADUL));
                Assert.That(info.Guid.Lo, Is.EqualTo(0xBEEFUL));
                Assert.That(info.State, Is.EqualTo(SceneLoadState.Loaded));
                Assert.That(info.RestoreMode, Is.EqualTo(SceneRestoreMode.Optional));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_MultipleScenes_AllPreserved()
        {
            var scenes = MakeScenes(5);

            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 256);
                SubSceneStateSection.Write(ref writer, scenes);

                int written = SubSceneStateLayout.ComputeBodySize(scenes.Length);
                var view = SubSceneStateSection.Read(ptr, written);
                Assert.That(view.Count, Is.EqualTo(5u));

                for (int i = 0; i < scenes.Length; i++)
                {
                    var info = SubSceneStateSection.GetSceneLoadInfo(view, i);
                    Assert.That(info.Guid, Is.EqualTo(scenes[i].Guid));
                    Assert.That(info.State, Is.EqualTo(scenes[i].State));
                    Assert.That(info.RestoreMode, Is.EqualTo(scenes[i].RestoreMode));
                }
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
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                Assert.Throws<FormatException>(() => SubSceneStateSection.Read(ptr, 4));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_TruncatedBody_Throws()
        {
            // 3 records require 68 bytes (8 header + 3×20).
            // Allocate 256 to let Write succeed, then feed Read only 28 bytes.
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 256);
                SubSceneStateSection.Write(ref writer, MakeScenes(3));

                // Body declares 3 records (68 bytes) but only 28 bytes are supplied.
                Assert.Throws<FormatException>(() => SubSceneStateSection.Read(ptr, 28));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_CountOverflow_Throws()
        {
            var buffer = new NativeArray<byte>(8, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                *(uint*)ptr = 1000000;
                Assert.Throws<FormatException>(() => SubSceneStateSection.Read(ptr, 8));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void GetGuid_ExtractsCorrectValues()
        {
            var scenes = new SceneLoadInfo[]
            {
                new SceneLoadInfo(new SceneGuid(0x1111, 0x2222), SceneLoadState.Loaded),
                new SceneLoadInfo(new SceneGuid(0x3333, 0x4444), SceneLoadState.Unloaded),
            };

            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var writer = new FormatWriter(ptr, 128);
                SubSceneStateSection.Write(ref writer, scenes);

                var view = SubSceneStateSection.Read(ptr, 48);
                Assert.That(SubSceneStateSection.GetGuid(view, 0).Hi, Is.EqualTo(0x1111UL));
                Assert.That(SubSceneStateSection.GetGuid(view, 0).Lo, Is.EqualTo(0x2222UL));
                Assert.That(SubSceneStateSection.GetGuid(view, 1).Hi, Is.EqualTo(0x3333UL));
                Assert.That(SubSceneStateSection.GetGuid(view, 1).Lo, Is.EqualTo(0x4444UL));
            }
            finally { buffer.Dispose(); }
        }
    }
}
