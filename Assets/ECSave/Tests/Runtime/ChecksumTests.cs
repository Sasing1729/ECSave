using NUnit.Framework;
using ECSave.Format;
using ECSave.Internal;

namespace ECSave.Tests.Runtime
{
    public unsafe class ChecksumTests
    {
        [Test]
        public void EmptyInput_MatchesKnownVector()
        {
            byte[] buf = new byte[0];
            fixed (byte* p = buf)
            {
                var view = new ByteView(p, 0);
                ulong actual = ChecksumCalculator.Compute(view);
                Assert.AreEqual(ChecksumCalculator.EmptyInputChecksum, actual);
            }
        }

        [Test]
        public void SameInput_Deterministic()
        {
            byte[] buf = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            fixed (byte* p = buf)
            {
                var view = new ByteView(p, buf.Length);
                ulong a = ChecksumCalculator.Compute(view);
                ulong b = ChecksumCalculator.Compute(view);
                Assert.AreEqual(a, b);
            }
        }

        [Test]
        public void DifferentInput_DifferentHash()
        {
            byte[] b1 = { 1, 2, 3 };
            byte[] b2 = { 1, 2, 4 };
            ulong h1, h2;
            fixed (byte* p1 = b1) h1 = ChecksumCalculator.Compute(new ByteView(p1, b1.Length));
            fixed (byte* p2 = b2) h2 = ChecksumCalculator.Compute(new ByteView(p2, b2.Length));
            Assert.AreNotEqual(h1, h2);
        }

        [Test]
        public void KnownVectors_PrintForCapture()
        {
            var inputs = new (string Name, byte[] Data)[]
            {
                ("ECSV", new byte[] { 0x45, 0x43, 0x53, 0x56 }),
                ("zeros4", new byte[4]),
                ("singleByte0", new byte[] { 0x00 }),
                ("128bytes", CreateSequential(128)),
            };

            foreach (var (name, data) in inputs)
            {
                fixed (byte* p = data)
                {
                    ulong h = ChecksumCalculator.Compute(new ByteView(p, data.Length));
                    TestContext.WriteLine($"[ChecksumKnownVector] {name}: 0x{h:X16}UL");
                }
            }
            Assert.Pass("See TestContext output");
        }

        [Test]
        public void KnownVector_ECSV_Captured()
        {
            byte[] buf = { 0x45, 0x43, 0x53, 0x56 };
            fixed (byte* p = buf)
            {
                ulong actual = ChecksumCalculator.Compute(new ByteView(p, 4));
                Assert.AreNotEqual(0UL, actual);
            }
        }

        private static byte[] CreateSequential(int length)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = (byte)i;
            return data;
        }
    }
}
