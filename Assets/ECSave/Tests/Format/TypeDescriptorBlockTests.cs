using NUnit.Framework;
using ECSave.Format;

namespace ECSave.Tests.Format
{
    public unsafe class TypeDescriptorBlockTests
    {
        [Test]
        public void Write_ThenReadBack()
        {
            // 入力列は 5.4 の正規エンコードに従う（先頭 u16 構造体サイズ + エントリ群）
            byte[] input = new byte[2 + 16]; // size + 1 entry
            input[0] = 0x10; // size = 16 (LE)
            input[1] = 0x00;
            input[2] = (byte)SchemaHashKind.Int32;
            string[] fieldNames = { "A" };

            int utf8Total = 1; // "A" = 1 byte
            int size = TypeDescriptorBlockWriter.ComputeSize(input.Length, fieldNames.Length, utf8Total);
            byte[] buf = new byte[size];

            fixed (byte* p = buf)
            fixed (byte* inputPtr = input)
            {
                var w = new FormatWriter(p, buf.Length);
                int written = TypeDescriptorBlockWriter.Write(
                    ref w,
                    entryCount: 1, rawEntryCount: 0,
                    inputPtr, input.Length,
                    fieldNames);
                Assert.AreEqual(size, written);
                Assert.AreEqual(size, w.Offset);

                // 読み戻し
                var view = new ByteView(p, size);
                var r = new ByteReader(view);
                uint blockLen = r.ReadU32();
                Assert.AreEqual((uint)size, blockLen);

                ushort ec = r.ReadU16();
                ushort rc = r.ReadU16();
                Assert.AreEqual(1, ec);
                Assert.AreEqual(0, rc);

                int il = r.ReadI32();
                Assert.AreEqual(input.Length, il);
            }
        }

        [Test]
        public void NameTooLong_Throws()
        {
            byte[] input = new byte[2];
            string[] names = { new string('x', 300) };
            int size = 1024;
            byte[] buf = new byte[size];

            bool threw = false;
            FormatException caught = null;

            fixed (byte* p = buf)
            fixed (byte* inputPtr = input)
            {
                try
                {
                    var w = new FormatWriter(p, buf.Length);
                    TypeDescriptorBlockWriter.Write(ref w, 1, 0, inputPtr, input.Length, names);
                }
                catch (FormatException ex)
                {
                    threw = true;
                    caught = ex;
                }
            }

            Assert.IsTrue(threw, "Expected FormatException");
            Assert.AreEqual(FormatError.ValidationFailed, caught.Error);
        }

        [Test]
        public void NameCountMismatch_Throws()
        {
            byte[] input = new byte[2];
            string[] names = { "A", "B" }; // EntryCount=1 と不一致
            byte[] buf = new byte[256];

            bool threw = false;
            FormatException caught = null;

            fixed (byte* p = buf)
            fixed (byte* inputPtr = input)
            {
                try
                {
                    var w = new FormatWriter(p, buf.Length);
                    TypeDescriptorBlockWriter.Write(ref w, 1, 0, inputPtr, input.Length, names);
                }
                catch (FormatException ex)
                {
                    threw = true;
                    caught = ex;
                }
            }

            Assert.IsTrue(threw, "Expected FormatException");
            Assert.AreEqual(FormatError.ValidationFailed, caught.Error);
        }
    }
}
