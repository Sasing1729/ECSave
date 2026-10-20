using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class SubSceneTombstoneSectionTests
    {
        private static byte[][] MakeKeys(int count, int stride)
        {
            var result = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                result[i] = new byte[stride];
                for (int b = 0; b < stride; b++)
                    result[i][b] = (byte)(i * 16 + b);
            }
            return result;
        }

        [Test]
        public void WriteRead_Empty_Roundtrip()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 64);
                SubSceneTombstoneSection.Write(ref w, System.Array.Empty<byte[]>(), 4);

                var v = SubSceneTombstoneSection.Read(p, 12);
                Assert.That(v.Count, Is.EqualTo(0u));
                Assert.That(v.KeyStride, Is.EqualTo(4));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_SingleKey_Roundtrip()
        {
            var keys = MakeKeys(1, 4);
            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 128);
                SubSceneTombstoneSection.Write(ref w, keys, 4);

                int written = SubSceneTombstoneLayout.ComputeBodySize(1, 4);
                var v = SubSceneTombstoneSection.Read(p, written);

                Assert.That(v.Count, Is.EqualTo(1u));
                Assert.That(v.KeyStride, Is.EqualTo(4));

                byte* key0 = v.GetKey(0);
                for (int b = 0; b < 4; b++)
                    Assert.That(key0[b], Is.EqualTo(keys[0][b]));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void WriteRead_MultipleKeys_Roundtrip()
        {
            var keys = MakeKeys(5, 8);
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 256);
                SubSceneTombstoneSection.Write(ref w, keys, 8);

                int written = SubSceneTombstoneLayout.ComputeBodySize(5, 8);
                var v = SubSceneTombstoneSection.Read(p, written);

                Assert.That(v.Count, Is.EqualTo(5u));
                Assert.That(v.KeyStride, Is.EqualTo(8));

                for (int i = 0; i < 5; i++)
                {
                    byte* k = v.GetKey(i);
                    for (int b = 0; b < 8; b++)
                        Assert.That(k[b], Is.EqualTo(keys[i][b]));
                }
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_NullPointer_Throws()
        {
            Assert.Throws<FormatException>(() => SubSceneTombstoneSection.Read(null, 100));
        }

        [Test]
        public void Read_TooShort_Throws()
        {
            var buffer = new NativeArray<byte>(4, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                Assert.Throws<FormatException>(() => SubSceneTombstoneSection.Read(p, 4));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_TruncatedBody_Throws()
        {
            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 128);
                SubSceneTombstoneSection.Write(ref w, MakeKeys(3, 4), 4);

                // Body declares 3 records (12 + 12 = 24 bytes) but only 16 supplied
                Assert.Throws<FormatException>(() => SubSceneTombstoneSection.Read(p, 16));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_KeyStrideOutOfRange_Throws()
        {
            var buffer = new NativeArray<byte>(32, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                *(uint*)p = 0;
                *(int*)(p + 4) = 100000;
                Assert.Throws<FormatException>(() => SubSceneTombstoneSection.Read(p, 12));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Read_NegativeKeyStride_Throws()
        {
            var buffer = new NativeArray<byte>(32, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                *(uint*)p = 0;
                *(int*)(p + 4) = -1;
                Assert.Throws<FormatException>(() => SubSceneTombstoneSection.Read(p, 12));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void Write_InvalidKeyLength_Throws()
        {
            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
                var w = new FormatWriter(p, 128);
                var keys = new byte[][]
                {
                    new byte[4],
                    new byte[3],
                };
                Assert.Throws<FormatException>(
                    () => SubSceneTombstoneSection.Write(ref w, keys, 4));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void SectionNameFor_DifferentTypeName_DifferentHash()
        {
            var h1 = SubSceneTombstoneSection.NameHashFor("A.B");
            var h2 = SubSceneTombstoneSection.NameHashFor("A.C");
            Assert.That(h1, Is.Not.EqualTo(h2));
        }

        [Test]
        public void SectionNameFor_SameTypeName_SameHash()
        {
            var h1 = SubSceneTombstoneSection.NameHashFor("A.B");
            var h2 = SubSceneTombstoneSection.NameHashFor("A.B");
            Assert.That(h1, Is.EqualTo(h2));
        }
    }
}
