using NUnit.Framework;
using ECSave.Format;

namespace ECSave.Tests.Format
{
    public unsafe class FormatReaderNameHeapTests
    {
        [Test]
        public void NameHeap_StartsAfterSectionTable()
        {
            // ヘッダ(0x30) + WorldId(0) + align + SectionTable(1 × 48) + NameHeap("A")
            byte[] buf = new byte[256];
            fixed (byte* p = buf)
            {
                const int headerSize = 0x30;
                const int sectionCount = 1;
                const int tableOffset = 0x30; // align(0x30, 8) = 0x30
                const int tableSize = sectionCount * 48;
                const int nameHeapOffset = tableOffset + tableSize;

                var header = new SaveHeader
                {
                    Magic = ECSaveFormatConstants.Magic,
                    FormatVersion = 1,
                    HeaderFlags = 0,
                    HeaderSize = headerSize,
                    GameDataVersion = 1,
                    LogicalEntityCount = 0,
                    SectionCount = sectionCount,
                    SectionTableOffset = tableOffset,
                    TotalLength = 256,
                    NameHeapLength = 1,
                    WorldIdLength = 0,
                    Reserved = 0,
                };
                var writer = new FormatWriter(p, buf.Length);
                writer.WriteHeader(header);

                writer.Seek(tableOffset);
                writer.WriteSectionEntry(new SectionEntry
                {
                    SectionId = 0,
                    Kind = SectionKind.Component,
                    Flags = 0,
                    NameLength = 1,
                    NameHash = 0,
                    SchemaHash = 0,
                    Offset = 0,
                    Length = 0,
                    NameOffset = 0,
                    Reserved = 0,
                });

                p[nameHeapOffset] = (byte)'A';

                var view = new ByteView(p, buf.Length);
                var reader = new FormatReader(view);
                reader.ReadHeader();
                var table = reader.ReadSectionTable();

                Assert.IsTrue(table.NameHeap != null, "NameHeap pointer should not be null");
                Assert.AreEqual(nameHeapOffset, (int)(table.NameHeap - p));
                Assert.AreEqual('A', (char)table.NameHeap[0]);
            }
        }
    }
}
