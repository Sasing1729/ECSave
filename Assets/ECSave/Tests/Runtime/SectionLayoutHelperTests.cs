using ECSave.Format;
using NUnit.Framework;

namespace ECSave.Tests.Runtime
{
    [TestFixture]
    public sealed class SectionLayoutHelperTests
    {
        [Test]
        public void RawToDataOffset_EmptyRefs_Identity()
        {
            Assert.AreEqual(0, SectionLayoutHelper.RawToDataOffset(0, null));
            Assert.AreEqual(16, SectionLayoutHelper.RawToDataOffset(16, null));
            Assert.AreEqual(100, SectionLayoutHelper.RawToDataOffset(100, new int[0]));
        }

        [Test]
        public void RawToDataOffset_RefAt0_ShiftsAfter()
        {
            // refs=[0]: raw 0（Entity 自身の先頭）は shift 対象外。
            // raw >= 4 は 4B 前に詰まる。
            var refs = new[] { 0 };
            Assert.AreEqual(0, SectionLayoutHelper.RawToDataOffset(0, refs));
            Assert.AreEqual(0, SectionLayoutHelper.RawToDataOffset(4, refs));
            Assert.AreEqual(12, SectionLayoutHelper.RawToDataOffset(16, refs));
            Assert.AreEqual(28, SectionLayoutHelper.RawToDataOffset(32, refs));
        }

        [Test]
        public void RawToDataOffset_RefAt32_ShiftsAfter()
        {
            var refs = new[] { 32 };
            Assert.AreEqual(0, SectionLayoutHelper.RawToDataOffset(0, refs));
            Assert.AreEqual(31, SectionLayoutHelper.RawToDataOffset(31, refs));
            Assert.AreEqual(32, SectionLayoutHelper.RawToDataOffset(32, refs));
            Assert.AreEqual(36, SectionLayoutHelper.RawToDataOffset(40, refs));
            Assert.AreEqual(40, SectionLayoutHelper.RawToDataOffset(44, refs));
        }

        [Test]
        public void RawToDataOffset_MultipleRefs_Accumulates()
        {
            var refs = new[] { 4, 16 };
            Assert.AreEqual(0, SectionLayoutHelper.RawToDataOffset(0, refs));
            Assert.AreEqual(4, SectionLayoutHelper.RawToDataOffset(4, refs));
            Assert.AreEqual(8, SectionLayoutHelper.RawToDataOffset(12, refs));
            Assert.AreEqual(12, SectionLayoutHelper.RawToDataOffset(16, refs));
            Assert.AreEqual(16, SectionLayoutHelper.RawToDataOffset(24, refs));
            Assert.AreEqual(20, SectionLayoutHelper.RawToDataOffset(28, refs));
        }

        [Test]
        public void DataToRawOffset_EmptyRefs_Identity()
        {
            Assert.AreEqual(0, SectionLayoutHelper.DataToRawOffset(0, null));
            Assert.AreEqual(16, SectionLayoutHelper.DataToRawOffset(16, null));
            Assert.AreEqual(100, SectionLayoutHelper.DataToRawOffset(100, new int[0]));
        }

        /// <summary>
        /// 往復恒等性は <b>data → raw → data</b> の方向でのみ成立する。
        /// （RawToDataOffset は多対一のため raw → data → raw は境界外で失敗する）
        /// </summary>
        [Test]
        public void RoundTrip_DataToRawToData_SingleRef()
        {
            var refs = new[] { 4 };
            for (int data = 0; data < 32; data++)
            {
                int raw = SectionLayoutHelper.DataToRawOffset(data, refs);
                int recovered = SectionLayoutHelper.RawToDataOffset(raw, refs);
                Assert.AreEqual(data, recovered,
                    $"Data→Raw→Data round-trip failed for data={data} (raw={raw})");
            }
        }

        [Test]
        public void RoundTrip_DataToRawToData_MultipleRefs()
        {
            var refs = new[] { 4, 16 };
            for (int data = 0; data < 40; data++)
            {
                int raw = SectionLayoutHelper.DataToRawOffset(data, refs);
                int recovered = SectionLayoutHelper.RawToDataOffset(raw, refs);
                Assert.AreEqual(data, recovered,
                    $"Data→Raw→Data round-trip failed for data={data} (raw={raw})");
            }
        }

        /// <summary>
        /// 境界値の明示的検証：DataToRawOffset が「最小の raw 前像」を返すこと。
        /// </summary>
        [Test]
        public void DataToRawOffset_ReturnsMinimalPreimage()
        {
            // refs=[4]:
            //   data=0..4 → raw=0..4（shift なし）
            //   data=5.. → raw=9..（shift 4）
            var refs = new[] { 4 };
            Assert.AreEqual(0, SectionLayoutHelper.DataToRawOffset(0, refs));
            Assert.AreEqual(1, SectionLayoutHelper.DataToRawOffset(1, refs));
            Assert.AreEqual(3, SectionLayoutHelper.DataToRawOffset(3, refs));
            Assert.AreEqual(4, SectionLayoutHelper.DataToRawOffset(4, refs));
            Assert.AreEqual(9, SectionLayoutHelper.DataToRawOffset(5, refs));
            Assert.AreEqual(10, SectionLayoutHelper.DataToRawOffset(6, refs));
            Assert.AreEqual(12, SectionLayoutHelper.DataToRawOffset(8, refs));
        }

        [Test]
        public void RawToDataOffset_ChunkMixedLayout()
        {
            // ChunkMixed: sizeof=44, refs=[32], dataStride=40
            // FixedString32Bytes.Name  : raw 0  → data 0
            // Entity.Target            : raw 32 → data 32（Entity 自身の先頭）
            // int.Value                : raw 40 → data 36
            var refs = new[] { 32 };

            Assert.AreEqual(0, SectionLayoutHelper.RawToDataOffset(0, refs));
            Assert.AreEqual(32, SectionLayoutHelper.RawToDataOffset(32, refs));
            Assert.AreEqual(36, SectionLayoutHelper.RawToDataOffset(40, refs));
        }
    }
}
