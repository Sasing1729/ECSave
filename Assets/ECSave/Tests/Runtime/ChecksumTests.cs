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
            // A-01：空入力（seed=0）は 0x2D06800538D394C2
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
    }
}
