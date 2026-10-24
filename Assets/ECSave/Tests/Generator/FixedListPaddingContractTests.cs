using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests.Generator
{
    [TestFixture]
    public sealed class FixedListPaddingContractTests
    {
        private static System.Type _fixedListType;
        private static MethodInfo _paddingBytesMethod;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var assembly = typeof(FixedList128Bytes<FixedString32Bytes>).Assembly;
            _fixedListType = assembly.GetType("Unity.Collections.FixedList");
            Assert.IsNotNull(_fixedListType, "Unity.Collections.FixedList type not found");

            _paddingBytesMethod = _fixedListType.GetMethod("PaddingBytes",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(_paddingBytesMethod, "PaddingBytes method not found");
        }

        private static int PaddingBytesOf<T>() where T : unmanaged
            => (int)_paddingBytesMethod.MakeGenericMethod(typeof(T)).Invoke(null, null);

        private static int ExpectedPadding(int size)
        {
            int rem = size % 8;
            if (rem == 0) rem = 8;
            int pad = rem - 2;
            return pad > 0 ? pad : 0;
        }

        private static unsafe int FindAscii(byte* ptr, int total, string needle)
        {
            var bytes = Encoding.ASCII.GetBytes(needle);
            for (int i = 0; i + bytes.Length <= total; i++)
            {
                bool match = true;
                for (int k = 0; k < bytes.Length; k++)
                {
                    if (ptr[i + k] != bytes[k]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        [Test]
        public void PaddingBytes_FixedString32_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<FixedString32Bytes>());
        }

        [Test]
        public void PaddingBytes_FixedString64_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<FixedString64Bytes>());
        }

        [Test]
        public void PaddingBytes_FixedString128_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<FixedString128Bytes>());
        }

        [Test]
        public void PaddingBytes_FixedString512_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<FixedString512Bytes>());
        }

        [Test]
        public void PaddingBytes_FixedString4096_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<FixedString4096Bytes>());
        }

        [Test]
        public void PaddingBytes_Int_Is2()
        {
            Assert.AreEqual(2, PaddingBytesOf<int>());
        }

        [Test]
        public void PaddingBytes_Byte_Is0()
        {
            Assert.AreEqual(0, PaddingBytesOf<byte>());
        }

        [Test]
        public void PaddingBytes_Long_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<long>());
        }

        [Test]
        public void PaddingBytes_Double_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<double>());
        }

        [Test]
        public void PaddingBytes_Entity_Is6()
        {
            Assert.AreEqual(6, PaddingBytesOf<Entity>());
        }

        [Test]
        public void PaddingBytes_Short_Is0()
        {
            Assert.AreEqual(0, PaddingBytesOf<short>());
        }

        [Test]
        public void PaddingBytes_Struct12_Is2()
        {
            Assert.AreEqual(12, UnsafeUtility.SizeOf<Size12Struct>());
            Assert.AreEqual(ExpectedPadding(12), PaddingBytesOf<Size12Struct>());
        }

        [Test]
        public void PaddingBytes_Struct34_Is0()
        {
            Assert.AreEqual(34, UnsafeUtility.SizeOf<Size34Struct>());
            Assert.AreEqual(ExpectedPadding(34), PaddingBytesOf<Size34Struct>());
        }

        [Test]
        public void PaddingBytes_Struct36_Is2()
        {
            Assert.AreEqual(36, UnsafeUtility.SizeOf<Size36Struct>());
            Assert.AreEqual(ExpectedPadding(36), PaddingBytesOf<Size36Struct>());
        }

        [Test]
        public void PaddingBytes_Struct40_Is6()
        {
            Assert.AreEqual(40, UnsafeUtility.SizeOf<Size40Struct>());
            Assert.AreEqual(ExpectedPadding(40), PaddingBytesOf<Size40Struct>());
        }

        [Test]
        public void PaddingBytes_Struct64_Is6()
        {
            Assert.AreEqual(64, UnsafeUtility.SizeOf<Size64Struct>());
            Assert.AreEqual(ExpectedPadding(64), PaddingBytesOf<Size64Struct>());
        }

        [Test]
        public unsafe void ActualLayout_FixedList128FixedString32_ElementDataOffset()
        {
            var list = new FixedList128Bytes<FixedString32Bytes>();
            list.Add("ABCDEFGH");
            list.Add("IJKLMNOP");

            byte* basePtr = (byte*)UnsafeUtility.AddressOf(ref list);
            int totalSize = UnsafeUtility.SizeOf<FixedList128Bytes<FixedString32Bytes>>();

            int found1 = FindAscii(basePtr, totalSize, "ABCDEFGH");
            int found2 = FindAscii(basePtr, totalSize, "IJKLMNOP");
            Assert.GreaterOrEqual(found1, 0, "ABCDEFGH not found in FixedList memory");
            Assert.GreaterOrEqual(found2, 0, "IJKLMNOP not found in FixedList memory");

            int structOffsetInElement = 0;
            int actualElementDataOffset = found1 - structOffsetInElement - 2;
            int expectedElementDataOffset = 2 + PaddingBytesOf<FixedString32Bytes>();

            Assert.AreEqual(expectedElementDataOffset, actualElementDataOffset,
                "Actual ElementDataOffset differs from 2 + PaddingBytes<FixedString32Bytes>");
            Assert.AreEqual(8, actualElementDataOffset);
            Assert.AreEqual(32, found2 - found1, "ElementStride mismatch");
        }

        [Test]
        public unsafe void ActualLayout_FixedList512FixedString32_ElementDataOffset()
        {
            var list = new FixedList512Bytes<FixedString32Bytes>();
            list.Add("XXXXYYYY");

            byte* basePtr = (byte*)UnsafeUtility.AddressOf(ref list);
            int totalSize = UnsafeUtility.SizeOf<FixedList512Bytes<FixedString32Bytes>>();

            int found = FindAscii(basePtr, totalSize, "XXXXYYYY");
            Assert.GreaterOrEqual(found, 0);

            int structOffsetInElement = 0;
            int actualElementDataOffset = found - structOffsetInElement - 2;
            Assert.AreEqual(2 + PaddingBytesOf<FixedString32Bytes>(), actualElementDataOffset);
            Assert.AreEqual(8, actualElementDataOffset);
        }

        [Test]
        public unsafe void ActualLayout_FixedList512Struct36_ElementDataOffset()
        {
            var list = new FixedList512Bytes<Size36Struct>();
            list.Add(new Size36Struct { Score = 10, Label = "ABCDEFGH" });

            byte* basePtr = (byte*)UnsafeUtility.AddressOf(ref list);
            int totalSize = UnsafeUtility.SizeOf<FixedList512Bytes<Size36Struct>>();

            int found = FindAscii(basePtr, totalSize, "ABCDEFGH");
            Assert.GreaterOrEqual(found, 0);

            int structOffsetInElement = UnsafeUtility.SizeOf<int>();
            int actualElementDataOffset = found - structOffsetInElement - 2;
            int expectedElementDataOffset = 2 + PaddingBytesOf<Size36Struct>();

            Assert.AreEqual(expectedElementDataOffset, actualElementDataOffset);
            Assert.AreEqual(4, actualElementDataOffset);
        }

        [Test]
        public unsafe void ActualLayout_FixedList512Struct40_ElementDataOffset()
        {
            var list = new FixedList512Bytes<Size40Struct>();
            list.Add(new Size40Struct { A = 100, Label = "ABCDEFGH" });

            byte* basePtr = (byte*)UnsafeUtility.AddressOf(ref list);
            int totalSize = UnsafeUtility.SizeOf<FixedList512Bytes<Size40Struct>>();

            int found = FindAscii(basePtr, totalSize, "ABCDEFGH");
            Assert.GreaterOrEqual(found, 0);

            int structOffsetInElement = UnsafeUtility.SizeOf<long>();
            int actualElementDataOffset = found - structOffsetInElement - 2;
            int expectedElementDataOffset = 2 + PaddingBytesOf<Size40Struct>();

            Assert.AreEqual(expectedElementDataOffset, actualElementDataOffset);
            Assert.AreEqual(8, actualElementDataOffset);
        }

        [Test]
        public unsafe void ActualLayout_FixedList512Struct34_ElementDataOffset()
        {
            var list = new FixedList512Bytes<Size34Struct>();
            list.Add(new Size34Struct { A = 7, B = "ABCDEFGH" });

            byte* basePtr = (byte*)UnsafeUtility.AddressOf(ref list);
            int totalSize = UnsafeUtility.SizeOf<FixedList512Bytes<Size34Struct>>();

            int found = FindAscii(basePtr, totalSize, "ABCDEFGH");
            Assert.GreaterOrEqual(found, 0);

            int structOffsetInElement = UnsafeUtility.SizeOf<short>();
            int actualElementDataOffset = found - structOffsetInElement - 2;
            int expectedElementDataOffset = 2 + PaddingBytesOf<Size34Struct>();

            Assert.AreEqual(expectedElementDataOffset, actualElementDataOffset);
            Assert.AreEqual(2, actualElementDataOffset);
        }

        [Test]
        public void PaddingBytes_AllFixedStringsMatchExpected()
        {
            Assert.AreEqual(ExpectedPadding(32), PaddingBytesOf<FixedString32Bytes>());
            Assert.AreEqual(ExpectedPadding(64), PaddingBytesOf<FixedString64Bytes>());
            Assert.AreEqual(ExpectedPadding(128), PaddingBytesOf<FixedString128Bytes>());
            Assert.AreEqual(ExpectedPadding(512), PaddingBytesOf<FixedString512Bytes>());
            Assert.AreEqual(ExpectedPadding(4096), PaddingBytesOf<FixedString4096Bytes>());
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Size12Struct
        {
            public int A;
            public int B;
            public int C;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Size34Struct
        {
            public short A;
            public FixedString32Bytes B;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Size36Struct
        {
            public int Score;
            public FixedString32Bytes Label;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Size40Struct
        {
            public long A;
            public FixedString32Bytes Label;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Size64Struct
        {
            public FixedString32Bytes A;
            public FixedString32Bytes B;
        }
    }
}
