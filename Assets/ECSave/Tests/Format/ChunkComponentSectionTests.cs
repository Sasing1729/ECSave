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
            uint[] memberOffset = { 0, 2, 5, 6 };
            uint[] memberEntityIndex = { 10, 20, 30, 40, 50, 60 };
            byte[] records = new byte[groupCount * stride];
            for (int i = 0; i < records.Length; i++) records[i] = (byte)(i + 1);

            int size = ChunkComponentSectionWriter.ComputeFixedBodySize(
                groupCount, memberEntityIndex.Length, stride);
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteFixed(
                    ref w, groupCount, stride, mo, memberEntityIndex.Length, mei, rec);
                Assert.AreEqual(size, w.Offset);

                var view = new ByteView(p, size);
                var v = ChunkComponentSectionReader.Read(view);
                Assert.AreEqual(groupCount, (int)v.Header.GroupCount);
                Assert.IsTrue(v.Header.IsFixedLength);
                for (int i = 0; i <= groupCount; i++)
                    Assert.AreEqual(memberOffset[i], v.MemberOffset[i]);
                for (int i = 0; i < memberEntityIndex.Length; i++)
                    Assert.AreEqual(memberEntityIndex[i], v.MemberEntityIndex[i]);
                for (int i = 0; i < records.Length; i++)
                    Assert.AreEqual(records[i], v.Records[i]);
            }
        }

        [Test]
        public void VariableBody_RoundTrip()
        {
            const int groupCount = 2;
            const int stride = 0;
            uint[] memberOffset = { 0, 1, 3 };
            uint[] memberEntityIndex = { 10, 20, 30 };
            uint[] recordOffset = { 0, 5, 12 };
            byte[] records = new byte[12];
            for (int i = 0; i < records.Length; i++) records[i] = (byte)(i + 1);

            int headerSize = ChunkComponentSectionWriter.ComputeVariableHeaderSize(
                groupCount, memberEntityIndex.Length);
            int size = headerSize + records.Length;
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (uint* ro = recordOffset)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteVariable(
                    ref w, groupCount, stride, mo, memberEntityIndex.Length, mei,
                    ro, rec, records.Length);
                Assert.AreEqual(size, w.Offset);

                var view = new ByteView(p, size);
                var v = ChunkComponentSectionReader.Read(view);
                Assert.AreEqual(groupCount, (int)v.Header.GroupCount);
                Assert.IsFalse(v.Header.IsFixedLength);
                Assert.AreEqual(records.Length, v.RecordsLength);
                for (int i = 0; i <= groupCount; i++)
                {
                    Assert.AreEqual(memberOffset[i], v.MemberOffset[i]);
                    Assert.AreEqual(recordOffset[i], v.RecordOffset[i]);
                }
            }
        }

        [Test]
        public void MemberOffset_NonIncreasing_Throws()
        {
            const int groupCount = 3;
            uint[] badOffset = { 0, 2, 2, 6 };
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
        public void GroupCountZero_Allowed()
        {
            byte[] buf = new byte[16];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, buf.Length);
                w.WriteU32(0);
                w.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);
                w.WriteU32(0);

                var view = new ByteView(p, buf.Length);
                var v = ChunkComponentSectionReader.Read(view);
                Assert.AreEqual(0, (int)v.Header.GroupCount);
                Assert.AreEqual(0, (int)v.MemberOffset[0]);
                Assert.AreEqual(0, v.RecordsLength);
            }
        }

        [Test]
        public void GroupCountZero_VariableLength_Allowed()
        {
            byte[] buf = new byte[20];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, buf.Length);
                w.WriteU32(0);
                w.WriteU32(0);
                w.WriteU32(8);

                var view = new ByteView(p, buf.Length);
                var v = ChunkComponentSectionReader.Read(view);
                Assert.AreEqual(0, (int)v.Header.GroupCount);
                Assert.AreEqual(0, (int)v.MemberOffset[0]);
                Assert.AreEqual(0, (int)v.RecordOffset[0]);
            }
        }

        [Test]
        public void GroupCount_MaxBoundary_Allowed()
        {
            const int groupCount = 64;
            uint[] memberOffset = new uint[groupCount + 1];
            uint[] memberEntityIndex = new uint[groupCount];
            byte[] records = new byte[groupCount * 4];

            for (int i = 0; i <= groupCount; i++) memberOffset[i] = (uint)i;
            for (int i = 0; i < groupCount; i++) memberEntityIndex[i] = (uint)(i * 100);

            int size = ChunkComponentSectionWriter.ComputeFixedBodySize(
                groupCount, groupCount, 4);
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteFixed(
                    ref w, groupCount, 4, mo, groupCount, mei, rec);

                var view = new ByteView(p, size);
                var v = ChunkComponentSectionReader.Read(view);
                Assert.AreEqual(groupCount, (int)v.Header.GroupCount);
            }
        }

        [Test]
        public void Variable_RecordOffset_NonMonotonic_Throws()
        {
            const int groupCount = 2;
            uint[] memberOffset = { 0, 1, 3 };
            uint[] memberEntityIndex = { 10, 20, 30 };
            uint[] badRecordOffset = { 0, 10, 5 };
            byte[] records = new byte[12];

            int headerSize = ChunkComponentSectionWriter.ComputeVariableHeaderSize(
                groupCount, memberEntityIndex.Length);
            int size = headerSize + records.Length;
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (uint* ro = badRecordOffset)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteVariable(
                    ref w, groupCount, 0, mo, memberEntityIndex.Length, mei,
                    ro, rec, records.Length);

                var view = new ByteView(p, size);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void Variable_RecordOffset_FirstNotZero_Throws()
        {
            const int groupCount = 2;
            uint[] memberOffset = { 0, 1, 3 };
            uint[] memberEntityIndex = { 10, 20, 30 };
            uint[] badRecordOffset = { 1, 5, 12 };
            byte[] records = new byte[12];

            int headerSize = ChunkComponentSectionWriter.ComputeVariableHeaderSize(
                groupCount, memberEntityIndex.Length);
            int size = headerSize + records.Length;
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (uint* ro = badRecordOffset)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteVariable(
                    ref w, groupCount, 0, mo, memberEntityIndex.Length, mei,
                    ro, rec, records.Length);

                var view = new ByteView(p, size);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void Variable_RecordOffset_ExceedsAvailable_Throws()
        {
            const int groupCount = 2;
            uint[] memberOffset = { 0, 1, 3 };
            uint[] memberEntityIndex = { 10, 20, 30 };
            uint[] badRecordOffset = { 0, 5, 200 };
            byte[] records = new byte[12];

            int headerSize = ChunkComponentSectionWriter.ComputeVariableHeaderSize(
                groupCount, memberEntityIndex.Length);
            int size = headerSize + records.Length;
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (uint* ro = badRecordOffset)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteVariable(
                    ref w, groupCount, 0, mo, memberEntityIndex.Length, mei,
                    ro, rec, records.Length);

                var view = new ByteView(p, size);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void Flags_UnknownBits_Throws()
        {
            byte[] buf = new byte[24];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, buf.Length);
                w.WriteU32(1);
                w.WriteU32(0x02);
                w.WriteU32(0);
                w.WriteU32(0);
                w.WriteU32(0);

                var view = new ByteView(p, buf.Length);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void Fixed_RecordsLengthMismatch_Throws()
        {
            const int groupCount = 2;
            const int stride = 4;
            uint[] memberOffset = { 0, 1, 2 };
            uint[] memberEntityIndex = { 10, 20 };
            byte[] records = new byte[groupCount * stride];

            int expectedSize = ChunkComponentSectionWriter.ComputeFixedBodySize(
                groupCount, 2, stride);
            byte[] buf = new byte[expectedSize];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteFixed(
                    ref w, groupCount, stride, mo, 2, mei, rec);

                var view = new ByteView(p, buf.Length - 1);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void Truncated_Header_Throws()
        {
            byte[] buf = new byte[8];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, buf.Length);
                w.WriteU32(1);
                w.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);

                var view = new ByteView(p, buf.Length);
                Assert.Throws<FormatException>(() => ChunkComponentSectionReader.Read(view));
            }
        }

        [Test]
        public void MemberOffset_StrictlyIncreasing_SingleMemberGroups()
        {
            const int groupCount = 3;
            uint[] memberOffset = { 0, 1, 2, 3 };
            uint[] memberEntityIndex = { 100, 200, 300 };
            byte[] records = new byte[groupCount * 4];

            int size = ChunkComponentSectionWriter.ComputeFixedBodySize(
                groupCount, 3, 4);
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* mo = memberOffset)
            fixed (uint* mei = memberEntityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                ChunkComponentSectionWriter.WriteFixed(ref w, groupCount, 4, mo, 3, mei, rec);

                var view = new ByteView(p, size);
                var v = ChunkComponentSectionReader.Read(view);
                Assert.AreEqual(3, (int)v.Header.GroupCount);
                for (int i = 0; i <= groupCount; i++)
                    Assert.AreEqual(memberOffset[i], v.MemberOffset[i]);
            }
        }
    }
}
