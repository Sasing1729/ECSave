using NUnit.Framework;
using ECSave.Format;

namespace ECSave.Tests.Format
{
    public unsafe class CommonSectionTests
    {
        [Test]
        public void FixedBody_NoEnabled_RoundTrip()
        {
            const int count = 5;
            const int stride = 8;

            int size = CommonSectionWriter.ComputeFixedBodySize(count, stride, hasEnabledBits: false);
            byte[] buf = new byte[size];
            uint[] entityIndex = { 10, 20, 30, 40, 50 };
            byte[] records = new byte[count * stride];
            for (int i = 0; i < records.Length; i++) records[i] = (byte)(i + 1);

            fixed (byte* p = buf)
            fixed (uint* ei = entityIndex)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                CommonSectionWriter.WriteFixed(ref w, count, stride, ei, null, rec);
                Assert.AreEqual(size, w.Offset);

                var view = new ByteView(p, size);
                var v = CommonSectionReader.Read(view);
                Assert.AreEqual(count, (int)v.Header.Count);
                Assert.AreEqual(stride, (int)v.Header.Stride);
                Assert.IsTrue(v.Header.IsFixedLength);
                Assert.IsFalse(v.Header.HasEnabledBits);

                for (int i = 0; i < count; i++)
                {
                    Assert.AreEqual(entityIndex[i], v.EntityIndex[i]);
                }
                for (int i = 0; i < records.Length; i++)
                {
                    Assert.AreEqual(records[i], v.Records[i]);
                }
            }
        }

        [Test]
        public void FixedBody_WithEnabled_RoundTrip()
        {
            const int count = 10;
            const int stride = 4;

            int size = CommonSectionWriter.ComputeFixedBodySize(count, stride, hasEnabledBits: true);
            byte[] buf = new byte[size];
            uint[] entityIndex = new uint[count];
            for (int i = 0; i < count; i++) entityIndex[i] = (uint)i;
            byte[] enabledBits = { 0xAA, 0x02 };
            byte[] records = new byte[count * stride];

            fixed (byte* p = buf)
            fixed (uint* ei = entityIndex)
            fixed (byte* eb = enabledBits)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                CommonSectionWriter.WriteFixed(ref w, count, stride, ei, eb, rec);

                var view = new ByteView(p, size);
                var v = CommonSectionReader.Read(view);
                Assert.IsTrue(v.Header.HasEnabledBits);
                Assert.AreEqual(0xAA, v.EnabledBits[0]);
                Assert.AreEqual(0x02, v.EnabledBits[1]);
            }
        }

        [Test]
        public void VariableBody_RoundTrip()
        {
            const int count = 3;
            const int stride = 0;
            uint[] entityIndex = { 1, 2, 3 };
            uint[] recordOffset = { 0, 4, 12, 20 };
            byte[] records = new byte[20];
            for (int i = 0; i < records.Length; i++) records[i] = (byte)(i & 0x7F);

            int size = CommonSectionWriter.ComputeVariableHeaderSize(count, hasEnabledBits: false) + records.Length;
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* ei = entityIndex)
            fixed (uint* ro = recordOffset)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                CommonSectionWriter.WriteVariable(ref w, count, stride, ei, ro, null, rec, records.Length);

                var view = new ByteView(p, size);
                var v = CommonSectionReader.Read(view);
                Assert.IsFalse(v.Header.IsFixedLength);
                Assert.AreEqual(20, v.RecordsLength);
                for (int i = 0; i <= count; i++)
                    Assert.AreEqual(recordOffset[i], v.RecordOffset[i]);
            }
        }

        [Test]
        public void VariableBody_NonMonotonic_Throws()
        {
            uint[] entityIndex = { 1, 2, 3 };
            uint[] badOffset = { 0, 10, 5, 20 };
            byte[] records = new byte[20];
            int size = CommonSectionWriter.ComputeVariableHeaderSize(3, false) + records.Length;
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (uint* ei = entityIndex)
            fixed (uint* ro = badOffset)
            fixed (byte* rec = records)
            {
                var w = new FormatWriter(p, buf.Length);
                CommonSectionWriter.WriteVariable(ref w, 3, 0, ei, ro, null, rec, records.Length);

                var view = new ByteView(p, size);
                Assert.Throws<FormatException>(() => CommonSectionReader.Read(view));
            }
        }

        [Test]
        public void FixedBody_Truncated_Throws()
        {
            int size = CommonSectionWriter.ComputeFixedBodySize(5, 8, false);
            byte[] buf = new byte[size];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, size);
                w.WriteU32(5);
                w.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);
                w.WriteU32(8);

                var view = new ByteView(p, w.Offset);
                Assert.Throws<FormatException>(() => CommonSectionReader.Read(view));
            }
        }
    }
}
