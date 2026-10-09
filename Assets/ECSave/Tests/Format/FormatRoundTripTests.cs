// Tests/Format/FormatRoundTripTests.cs（抜粋）
using ECSave.Format;
using NUnit.Framework;
using System;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Tests.Format
{
    public unsafe class FormatRoundTripTests
    {
        [Test]
        public void Header_RoundTrip()
        {
            byte[] buf = new byte[128];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, buf.Length);
                var header = new SaveHeader
                {
                    Magic = ECSaveFormatConstants.Magic,
                    FormatVersion = ECSaveFormatConstants.CurrentFormatVersion,
                    HeaderFlags = 0,
                    HeaderSize = ECSaveFormatConstants.HeaderFixedSize + 5,
                    GameDataVersion = 1,
                    LogicalEntityCount = 0,
                    SectionCount = 0,
                    SectionTableOffset = 0,
                    TotalLength = 128,
                    NameHeapLength = 0,
                    WorldIdLength = 5,
                    Reserved = 0,
                };
                w.WriteHeader(header);
                // WorldId "hello"
                w.WriteU8((byte)'h'); w.WriteU8((byte)'e'); w.WriteU8((byte)'l');
                w.WriteU8((byte)'l'); w.WriteU8((byte)'o');

                var view = new ByteView(p, 128);
                var r = new FormatReader(view);
                var h = r.ReadHeader();
                Assert.AreEqual(ECSaveFormatConstants.Magic, h.Magic);
                Assert.AreEqual(1, h.FormatVersion);
                Assert.AreEqual(5, h.WorldIdLength);
            }
        }

        [Test]
        public void Header_MagicMismatch_Throws()
        {
            byte[] buf = new byte[128];
            fixed (byte* p = buf)
            {
                // Magic を書かない（0 のまま）
                var view = new ByteView(p, 128);
                var r = new FormatReader(view);
                Assert.Throws<FormatException>(() => r.ReadHeader());
            }
        }

        [Test]
        public void SchemaHashCore_IsDeterministic()
        {
            byte[] d1 = { 1, 2, 3, 4, 5 };
            byte[] d2 = { 1, 2, 3, 4, 5 };
            Assert.AreEqual(
                SchemaHashCore.Hash(d1),
                SchemaHashCore.Hash(d2));
        }
    }

}
