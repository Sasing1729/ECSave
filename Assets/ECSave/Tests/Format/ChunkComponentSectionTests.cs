using NUnit.Framework;
using ECSave.Format;

namespace ECSave.Tests.Format
{
    public unsafe class ChunkComponentSectionTests
    {
        [Test]
        public void FixedBody_RoundTrip()
        {
            const int groupCount = 3;
            const int stride = 4;
            uint[] memberOffset = { 0, 2, 5, 6 };    // 3 群、合計 6 メンバー
            uint[] memberEntityIndex = { 10, 20, 30, 40, 50, 60 };
            byte[] records = new byte[groupCount * stride];
            for (int i = 0; i < records.Length; i++) records[i] = (byte)(i + 1);

            int size = ChunkComponentSectionWriter.ComputeFixedBodySize(groupCount, memberEntityIndex.Length, stride);
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteFixed(ref w, groupCount, stride, mo, memberEntityIndex.Length, mei, rec);
                Assert.AreEqual(size, w.Offset);

                var view = new ByteView(p, size);
                var v = ChunkComponentSectionReader.Read(view);
                Assert.AreEqual(groupCount, (int)v.Header.GroupCount);
                Assert.IsTrue(v.Header.IsFixedLength);
                for (int i = 0; i <= groupCount; i++)
                    Assert.AreEqual(memberOffset[i], v.MemberOffset[i]);
                for (int i = 0; i < memberEntityIndex.Length; i++)
                    Assert.AreEqual(memberEntityIndex[i], v.MemberEntityIndex[i]);
            }
        }

        [Test]
        public void MemberOffset_NonIncreasing_Throws()
        {
            const int groupCount = 3;
            uint[] badOffset = { 0, 2, 2, 6 }; // 2 → 2 で非増加
            uint[] memberEntityIndex = new uint[6];
            byte[] records = new byte[groupCount * 4];
            int size = ChunkComponentSectionWriter.ComputeFixedBodySize(groupCount, 6, 4);
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = badOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteFixed(ref w, groupCount, 4, mo, 6, mei, rec);

                var view = new ByteView(p, size);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void MemberOffset_FirstNotZero_Throws()
        {
            uint[] badOffset = { 1, 2, 5, 6 };
            uint[] memberEntityIndex = new uint[6];
            byte[] records = new byte[3 * 4];
            int size = ChunkComponentSectionWriter.ComputeFixedBodySize(3, 6, 4);
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = badOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteFixed(ref w, 3, 4, mo, 6, mei, rec);

                var view = new ByteView(p, size);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void GroupCountZero_Throws()
        {
            byte[] buf = new byte[12 + 4];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, buf.Length);
                w.WriteU32(0);  // GroupCount = 0
                w.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);
                w.WriteU32(0);

                var view = new ByteView(p, buf.Length);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }
    }
}
