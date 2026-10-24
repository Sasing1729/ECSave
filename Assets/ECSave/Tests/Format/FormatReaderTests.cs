using System;
using System.Text;
using NUnit.Framework;
using ECSave.Format;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Tests.Format
{
    // ══════════════════════════════════════════════════════════════════
    //  Builder + safe read/tamper API
    //  （unsafe はこのクラス内部のみに閉じ込める）
    // ══════════════════════════════════════════════════════════════════

    internal static unsafe class FormatTestBuilder
    {
        public const int HeaderSize = 48;
        public const int SectionEntrySize = 48;
        public const int FooterSize = 8;

        // ── Safe snapshot types ──────────────────────────────────────

        public readonly struct HeaderSnapshot
        {
            public readonly uint Magic;
            public readonly ushort FormatVersion;
            public readonly ushort HeaderFlags;
            public readonly uint HeaderSizeField;
            public readonly int GameDataVersion;
            public readonly int LogicalEntityCount;
            public readonly int SectionCount;
            public readonly ulong SectionTableOffset;
            public readonly ulong TotalLength;
            public readonly uint NameHeapLength;
            public readonly ushort WorldIdLength;
            public readonly ushort Reserved;
            public readonly string WorldId;

            public HeaderSnapshot(in SaveHeader h, string worldId)
            {
                Magic = h.Magic;
                FormatVersion = h.FormatVersion;
                HeaderFlags = h.HeaderFlags;
                HeaderSizeField = h.HeaderSize;
                GameDataVersion = h.GameDataVersion;
                LogicalEntityCount = h.LogicalEntityCount;
                SectionCount = h.SectionCount;
                SectionTableOffset = h.SectionTableOffset;
                TotalLength = h.TotalLength;
                NameHeapLength = h.NameHeapLength;
                WorldIdLength = h.WorldIdLength;
                Reserved = h.Reserved;
                WorldId = worldId;
            }
        }

        public readonly struct SectionEntrySnapshot
        {
            public readonly uint SectionId;
            public readonly SectionKind Kind;
            public readonly byte Flags;
            public readonly ushort NameLength;
            public readonly ulong NameHash;
            public readonly ulong SchemaHash;
            public readonly ulong Offset;
            public readonly ulong Length;
            public readonly uint NameOffset;
            public readonly uint Reserved;
            public readonly bool HasTypeDescriptor;

            public SectionEntrySnapshot(in SectionEntry e)
            {
                SectionId = e.SectionId;
                Kind = e.Kind;
                Flags = e.Flags;
                NameLength = e.NameLength;
                NameHash = e.NameHash;
                SchemaHash = e.SchemaHash;
                Offset = e.Offset;
                Length = e.Length;
                NameOffset = e.NameOffset;
                Reserved = e.Reserved;
                HasTypeDescriptor = e.HasTypeDescriptor;
            }
        }

        public sealed class TypeDescriptorSnapshot
        {
            public ushort EntryCount { get; set; }
            public ushort RawEntryCount { get; set; }
            public int SchemaHashInputLength { get; set; }
            public int NameHeapLength { get; set; }
            public byte[] SchemaHashInput { get; set; }
            public byte[] NameHeap { get; set; }
        }

        public readonly struct RangeSnapshot
        {
            public readonly ulong Offset;
            public readonly ulong Length;
            public readonly SectionKind Kind;

            public RangeSnapshot(ulong offset, ulong length, SectionKind kind)
            {
                Offset = offset;
                Length = length;
                Kind = kind;
            }
        }

        // ── File builders ────────────────────────────────────────────

        public static byte[] NewMinimalValid()
        {
            var buf = new byte[HeaderSize + FooterSize];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, buf.Length);
                w.WriteHeader(new SaveHeader
                {
                    Magic = ECSaveFormatConstants.Magic,
                    FormatVersion = ECSaveFormatConstants.CurrentFormatVersion,
                    HeaderFlags = 0,
                    HeaderSize = HeaderSize,
                    GameDataVersion = 1,
                    LogicalEntityCount = 0,
                    SectionCount = 0,
                    SectionTableOffset = HeaderSize,
                    TotalLength = (ulong)buf.Length,
                    NameHeapLength = 0,
                    WorldIdLength = 0,
                    Reserved = 0,
                });
                w.WriteFooter(0);
            }
            return buf;
        }

        public static byte[] NewMinimalWithWorldId(string worldId)
        {
            var worldBytes = Encoding.UTF8.GetBytes(worldId);
            int worldIdEnd = HeaderSize + worldBytes.Length;
            int footerStart = AlignUp(worldIdEnd, 8);
            int total = footerStart + FooterSize;

            var buf = new byte[total];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, total);
                w.WriteHeader(new SaveHeader
                {
                    Magic = ECSaveFormatConstants.Magic,
                    FormatVersion = 1,
                    HeaderFlags = 0,
                    HeaderSize = (uint)worldIdEnd,
                    GameDataVersion = 1,
                    LogicalEntityCount = 0,
                    SectionCount = 0,
                    SectionTableOffset = (ulong)footerStart,
                    TotalLength = (ulong)total,
                    NameHeapLength = 0,
                    WorldIdLength = (ushort)worldBytes.Length,
                    Reserved = 0,
                });
                if (worldBytes.Length > 0)
                {
                    fixed (byte* wp = worldBytes)
                        w.WriteBytes(wp, worldBytes.Length);
                }
                w.Seek(footerStart);
                w.WriteFooter(0);
            }
            return buf;
        }

        public static byte[] NewWithOneEmptySection()
        {
            // header(48) + table(48) + section(0) + footer(8) = 104
            var buf = new byte[104];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, 104);
                w.WriteHeader(new SaveHeader
                {
                    Magic = ECSaveFormatConstants.Magic,
                    FormatVersion = 1,
                    HeaderFlags = 0,
                    HeaderSize = HeaderSize,
                    GameDataVersion = 1,
                    LogicalEntityCount = 0,
                    SectionCount = 1,
                    SectionTableOffset = HeaderSize,
                    TotalLength = 104,
                    NameHeapLength = 0,
                    WorldIdLength = 0,
                    Reserved = 0,
                });
                w.Seek(HeaderSize);
                w.WriteSectionEntry(new SectionEntry
                {
                    SectionId = 0,
                    Kind = SectionKind.Component,
                    Flags = 0,
                    NameLength = 0,
                    NameHash = 0,
                    SchemaHash = 0,
                    Offset = 96,
                    Length = 0,
                    NameOffset = 0,
                    Reserved = 0,
                });
                w.Seek(96);
                w.WriteFooter(0);
            }
            return buf;
        }

        public static byte[] NewWithMultipleSections(int[] kinds)
        {
            int n = kinds.Length;
            int tableSize = n * SectionEntrySize;
            int sectionsStart = HeaderSize + tableSize;
            int footerStart = AlignUp(sectionsStart, 8);
            int total = footerStart + FooterSize;

            var buf = new byte[total];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, total);
                w.WriteHeader(new SaveHeader
                {
                    Magic = ECSaveFormatConstants.Magic,
                    FormatVersion = 1,
                    HeaderFlags = 0,
                    HeaderSize = HeaderSize,
                    GameDataVersion = 1,
                    LogicalEntityCount = 0,
                    SectionCount = n,
                    SectionTableOffset = HeaderSize,
                    TotalLength = (ulong)total,
                    NameHeapLength = 0,
                    WorldIdLength = 0,
                    Reserved = 0,
                });
                w.Seek(HeaderSize);
                for (int i = 0; i < n; i++)
                {
                    w.WriteSectionEntry(new SectionEntry
                    {
                        SectionId = (uint)i,
                        Kind = (SectionKind)kinds[i],
                        Flags = 0,
                        NameLength = 0,
                        NameHash = 0,
                        SchemaHash = 0,
                        Offset = (ulong)sectionsStart,
                        Length = 0,
                        NameOffset = 0,
                        Reserved = 0,
                    });
                }
                w.Seek(footerStart);
                w.WriteFooter(0);
            }
            return buf;
        }

        public static byte[] NewWithTypeDescriptor(
    int blockLen = 24,
    ushort entryCount = 1,
    ushort rawEntryCount = 0,
    int inputLen = 2,
    byte[] input = null,
    int nameLen = 2,
    byte[] names = null,
    byte flags = ECSaveFormatConstants.SectionEntryFlags_HasTypeDescriptor,
    int? sectionLengthOverride = null)
        {
            input = input ?? new byte[] { 0x08, 0x00 };
            names = names ?? new byte[] { 0x01, (byte)'X' };

            // バッファサイズは「実際に書き込む内容長」と「blockLen フィールドに
            // 書く値」の大きい方で決める。blockLen を意図的に小さく/大きくしても、
            // 実際の書き込みが破綻しないようにする。
            //
            //   [blockLen u32][entryCount u16][rawEntryCount u16][inputLen i32]
            //   [input bytes][align8][nameLen i32][names bytes][align8]
            int inputEnd = 4 + 2 + 2 + 4 + inputLen;
            int inputAlign = AlignUp(inputEnd, 8) - inputEnd;
            int beforeNameLen = inputEnd + inputAlign;
            int afterNameLen = beforeNameLen + 4 + nameLen;
            int nameAlign = AlignUp(afterNameLen, 8) - afterNameLen;
            int minContentBytes = afterNameLen + nameAlign;

            int rawSectionSize = Math.Max(minContentBytes, blockLen);
            int sectionBlockSize = Math.Max(8, AlignUp(rawSectionSize, 8));

            int sectionStart = HeaderSize + SectionEntrySize;
            int footerStart = AlignUp(sectionStart + sectionBlockSize, 8);
            int total = footerStart + FooterSize;
            int reportedSectionLen = sectionLengthOverride ?? sectionBlockSize;

            var buf = new byte[total];
            fixed (byte* p = buf)
            {
                var w = new FormatWriter(p, total);
                w.WriteHeader(new SaveHeader
                {
                    Magic = ECSaveFormatConstants.Magic,
                    FormatVersion = 1,
                    HeaderFlags = 0,
                    HeaderSize = HeaderSize,
                    GameDataVersion = 1,
                    LogicalEntityCount = 0,
                    SectionCount = 1,
                    SectionTableOffset = HeaderSize,
                    TotalLength = (ulong)total,
                    NameHeapLength = 0,
                    WorldIdLength = 0,
                    Reserved = 0,
                });
                w.Seek(HeaderSize);
                w.WriteSectionEntry(new SectionEntry
                {
                    SectionId = 0,
                    Kind = SectionKind.Component,
                    Flags = flags,
                    NameLength = 0,
                    NameHash = 0,
                    SchemaHash = 0,
                    Offset = (ulong)sectionStart,
                    Length = (ulong)reportedSectionLen,
                    NameOffset = 0,
                    Reserved = 0,
                });
                w.Seek(sectionStart);
                // blockLen フィールド: 引数の値をそのまま書く
                // （実際の内容長と異なっていても、テストのためにそのまま書く）
                w.WriteU32((uint)blockLen);
                w.WriteU16(entryCount);
                w.WriteU16(rawEntryCount);
                w.WriteI32(inputLen);
                int toWrite = Math.Min(inputLen, input.Length);
                if (toWrite > 0)
                {
                    fixed (byte* ip = input)
                        w.WriteBytes(ip, toWrite);
                }
                if (inputLen > toWrite) w.Zero(inputLen - toWrite);
                w.AlignTo(8);
                w.WriteI32(nameLen);
                int nameToWrite = Math.Min(nameLen, names.Length);
                if (nameToWrite > 0)
                {
                    fixed (byte* np = names)
                        w.WriteBytes(np, nameToWrite);
                }
                if (nameLen > nameToWrite) w.Zero(nameLen - nameToWrite);
                w.AlignTo(8);
                w.Seek(footerStart);
                w.WriteFooter(0);
            }
            return buf;
        }

        public static int AlignUp(int v, int a) => (v + a - 1) & ~(a - 1);

        // ── Tampering (safe) ─────────────────────────────────────────

        public static void WriteU8(byte[] buf, int offset, byte v) => buf[offset] = v;

        public static void WriteU16(byte[] buf, int offset, ushort v)
        {
            buf[offset + 0] = (byte)v;
            buf[offset + 1] = (byte)(v >> 8);
        }

        public static void WriteU32(byte[] buf, int offset, uint v)
        {
            buf[offset + 0] = (byte)v;
            buf[offset + 1] = (byte)(v >> 8);
            buf[offset + 2] = (byte)(v >> 16);
            buf[offset + 3] = (byte)(v >> 24);
        }

        public static void WriteI32(byte[] buf, int offset, int v)
            => WriteU32(buf, offset, unchecked((uint)v));

        public static void WriteU64(byte[] buf, int offset, ulong v)
        {
            for (int i = 0; i < 8; i++) buf[offset + i] = (byte)(v >> (i * 8));
        }

        // ── Read (safe: pointer access is encapsulated) ──────────────

        public static HeaderSnapshot ReadHeader(byte[] buf)
        {
            fixed (byte* p = buf)
            {
                var reader = new FormatReader(new ByteView(p, buf.Length));
                var h = reader.ReadHeader();
                string worldId = h.WorldIdLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(h.WorldIdPtr, h.WorldIdLength);
                return new HeaderSnapshot(in h, worldId);
            }
        }

        public static string ReadWorldId(byte[] buf) => ReadHeader(buf).WorldId;

        public static (int count, int nameHeapLength) ReadTableInfo(byte[] buf)
        {
            fixed (byte* p = buf)
            {
                var reader = new FormatReader(new ByteView(p, buf.Length));
                reader.ReadHeader();
                var table = reader.ReadSectionTable();
                return (table.Count, table.NameHeapLength);
            }
        }

        public static SectionEntrySnapshot ReadEntry(byte[] buf, int idx)
        {
            fixed (byte* p = buf)
            {
                var reader = new FormatReader(new ByteView(p, buf.Length));
                reader.ReadHeader();
                var table = reader.ReadSectionTable();
                return new SectionEntrySnapshot(in table.Entries[idx]);
            }
        }

        public static TypeDescriptorSnapshot ReadDescriptor(byte[] buf, int idx)
        {
            fixed (byte* p = buf)
            {
                var reader = new FormatReader(new ByteView(p, buf.Length));
                reader.ReadHeader();
                var block = reader.ReadTypeDescriptor(idx);

                var input = new byte[block.SchemaHashInputLength];
                for (int i = 0; i < input.Length; i++) input[i] = block.SchemaHashInput[i];
                var names = new byte[block.NameHeapLength];
                for (int i = 0; i < names.Length; i++) names[i] = block.NameHeap[i];

                return new TypeDescriptorSnapshot
                {
                    EntryCount = block.EntryCount,
                    RawEntryCount = block.RawEntryCount,
                    SchemaHashInputLength = block.SchemaHashInputLength,
                    NameHeapLength = block.NameHeapLength,
                    SchemaHashInput = input,
                    NameHeap = names,
                };
            }
        }

        public static RangeSnapshot ReadRange(byte[] buf, int idx)
        {
            fixed (byte* p = buf)
            {
                var reader = new FormatReader(new ByteView(p, buf.Length));
                reader.ReadHeader();
                reader.GetSectionRange(idx, out ulong offset, out ulong length, out SectionKind kind);
                return new RangeSnapshot(offset, length, kind);
            }
        }

        // ── Error capture (safe) ─────────────────────────────────────

        public static FormatException ReadHeaderExpectError(byte[] buf)
            => Assert.Throws<FormatException>(() => ReadHeader(buf));

        public static FormatException ReadTableExpectError(byte[] buf)
            => Assert.Throws<FormatException>(() => ReadTableInfo(buf));

        public static FormatException ReadDescriptorExpectError(byte[] buf, int idx)
            => Assert.Throws<FormatException>(() => ReadDescriptor(buf, idx));

        public static FormatException ReadRangeExpectError(byte[] buf, int idx)
            => Assert.Throws<FormatException>(() => ReadRange(buf, idx));
    }

    // ══════════════════════════════════════════════════════════════════
    //  ReadHeader
    // ══════════════════════════════════════════════════════════════════

    [TestFixture]
    public sealed class FormatReaderReadHeaderTests
    {
        [Test]
        public void ValidMinimal_ReturnsHeader()
        {
            var h = FormatTestBuilder.ReadHeader(FormatTestBuilder.NewMinimalValid());
            Assert.AreEqual(ECSaveFormatConstants.Magic, h.Magic);
            Assert.AreEqual(ECSaveFormatConstants.CurrentFormatVersion, h.FormatVersion);
            Assert.AreEqual(0, h.SectionCount);
            Assert.AreEqual(0, h.LogicalEntityCount);
            Assert.AreEqual(0, h.WorldIdLength);
            Assert.AreEqual(48u, h.HeaderSizeField);
            Assert.AreEqual(56UL, h.TotalLength);
        }

        [Test]
        public void ValidWithWorldId_ReturnsHeader()
        {
            var h = FormatTestBuilder.ReadHeader(
                FormatTestBuilder.NewMinimalWithWorldId("WorldA"));
            Assert.AreEqual(6, h.WorldIdLength);
            Assert.AreEqual(54u, h.HeaderSizeField);
            Assert.AreEqual("WorldA", h.WorldId);
        }

        [Test]
        public void WorldId_ContentMatchesUtf8()
        {
            Assert.AreEqual("WorldA",
                FormatTestBuilder.ReadWorldId(FormatTestBuilder.NewMinimalWithWorldId("WorldA")));
        }

        [Test]
        public void WorldId_Utf8MultiByte()
        {
            Assert.AreEqual("世界A",
                FormatTestBuilder.ReadWorldId(FormatTestBuilder.NewMinimalWithWorldId("世界A")));
        }

        [Test]
        public void ReadTwice_ReturnsEquivalentHeader()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            var h1 = FormatTestBuilder.ReadHeader(buf);
            var h2 = FormatTestBuilder.ReadHeader(buf);
            Assert.AreEqual(h1.Magic, h2.Magic);
            Assert.AreEqual(h1.FormatVersion, h2.FormatVersion);
            Assert.AreEqual(h1.TotalLength, h2.TotalLength);
        }

        [Test]
        public void InputSize0_Throws()
        {
            var ex = FormatTestBuilder.ReadHeaderExpectError(new byte[0]);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void InputSize55_Throws()
        {
            var ex = FormatTestBuilder.ReadHeaderExpectError(new byte[55]);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void InputSize56_BoundaryPasses()
        {
            var h = FormatTestBuilder.ReadHeader(FormatTestBuilder.NewMinimalValid());
            Assert.AreEqual(56UL, h.TotalLength);
        }

        [TestCase(0u)]
        [TestCase(0x56534344u)]
        [TestCase(0x56534346u)]
        [TestCase(0xFFFFFFFFu)]
        public void Magic_Wrong_Throws(uint wrong)
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU32(buf, 0x00, wrong);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [TestCase((ushort)1)]
        [TestCase((ushort)0x8000)]
        [TestCase(ushort.MaxValue)]
        public void HeaderFlags_NonZero_Throws(ushort flags)
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x06, flags);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [TestCase((ushort)1)]
        [TestCase(ushort.MaxValue)]
        public void Reserved_NonZero_Throws(ushort reserved)
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x2E, reserved);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void FormatVersion_Zero_ReturnsVersionMismatch()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x04, 0);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.VersionMismatch, ex.Error);
        }

        [Test]
        public void FormatVersion_Newer_ReturnsVersionMismatch()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x04,
                (ushort)(ECSaveFormatConstants.CurrentFormatVersion + 1));
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.VersionMismatch, ex.Error);
        }

        [Test]
        public void FormatVersion_MaxUshort_ReturnsVersionMismatch()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x04, ushort.MaxValue);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.VersionMismatch, ex.Error);
        }

        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void LogicalEntityCount_Negative_Throws(int v)
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteI32(buf, 0x10, v);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void LogicalEntityCount_AtMax_Valid()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteI32(buf, 0x10, ECSaveFormatConstants.MaxLogicalEntityCount);
            var h = FormatTestBuilder.ReadHeader(buf);
            Assert.AreEqual(ECSaveFormatConstants.MaxLogicalEntityCount, h.LogicalEntityCount);
        }

        [Test]
        public void LogicalEntityCount_OverMax_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteI32(buf, 0x10, ECSaveFormatConstants.MaxLogicalEntityCount + 1);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void LogicalEntityCount_IntMax_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteI32(buf, 0x10, int.MaxValue);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void SectionCount_Negative_Throws(int v)
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteI32(buf, 0x14, v);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void SectionCount_AtMax_Valid()
        {
            int tableSize = ECSaveFormatConstants.MaxSectionCount * 48;
            int total = 48 + tableSize + 8;
            var buf = new byte[total];
            FormatTestBuilder.WriteU32(buf, 0x00, ECSaveFormatConstants.Magic);
            FormatTestBuilder.WriteU16(buf, 0x04, 1);
            FormatTestBuilder.WriteU16(buf, 0x06, 0);
            FormatTestBuilder.WriteU32(buf, 0x08, 48);
            FormatTestBuilder.WriteI32(buf, 0x0C, 1);
            FormatTestBuilder.WriteI32(buf, 0x10, 0);
            FormatTestBuilder.WriteI32(buf, 0x14, ECSaveFormatConstants.MaxSectionCount);
            FormatTestBuilder.WriteU64(buf, 0x18, 48);
            FormatTestBuilder.WriteU64(buf, 0x20, (ulong)total);
            FormatTestBuilder.WriteU32(buf, 0x28, 0);
            FormatTestBuilder.WriteU16(buf, 0x2C, 0);
            FormatTestBuilder.WriteU16(buf, 0x2E, 0);

            var h = FormatTestBuilder.ReadHeader(buf);
            Assert.AreEqual(ECSaveFormatConstants.MaxSectionCount, h.SectionCount);
        }

        [Test]
        public void SectionCount_OverMax_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteI32(buf, 0x14, ECSaveFormatConstants.MaxSectionCount + 1);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void WorldIdLength_AtMax_Valid()
        {
            int worldIdLen = ECSaveFormatConstants.MaxWorldIdLength;
            int worldIdEnd = 48 + worldIdLen;
            int footerStart = FormatTestBuilder.AlignUp(worldIdEnd, 8);
            int total = footerStart + 8;
            var buf = new byte[total];
            FormatTestBuilder.WriteU32(buf, 0x00, ECSaveFormatConstants.Magic);
            FormatTestBuilder.WriteU16(buf, 0x04, 1);
            FormatTestBuilder.WriteU16(buf, 0x06, 0);
            FormatTestBuilder.WriteU32(buf, 0x08, (uint)worldIdEnd);
            FormatTestBuilder.WriteI32(buf, 0x0C, 1);
            FormatTestBuilder.WriteI32(buf, 0x10, 0);
            FormatTestBuilder.WriteI32(buf, 0x14, 0);
            FormatTestBuilder.WriteU64(buf, 0x18, (ulong)footerStart);
            FormatTestBuilder.WriteU64(buf, 0x20, (ulong)total);
            FormatTestBuilder.WriteU32(buf, 0x28, 0);
            FormatTestBuilder.WriteU16(buf, 0x2C, (ushort)worldIdLen);
            FormatTestBuilder.WriteU16(buf, 0x2E, 0);

            var h = FormatTestBuilder.ReadHeader(buf);
            Assert.AreEqual(worldIdLen, h.WorldIdLength);
        }

        [Test]
        public void WorldIdLength_OverMax_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x2C,
                (ushort)(ECSaveFormatConstants.MaxWorldIdLength + 1));
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void WorldIdLength_MaxUshort_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x2C, ushort.MaxValue);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void TotalLength_Smaller_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU64(buf, 0x20, 55);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void TotalLength_Larger_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU64(buf, 0x20, 57);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void WorldIdEnd_ExceedsView_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x2C, 9);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void HeaderSize_LessThanWorldIdEnd_Throws()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU16(buf, 0x2C, 8);
            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void ErrorPrecedence_MagicBeforeFormatVersion()
        {
            var buf = FormatTestBuilder.NewMinimalValid();
            FormatTestBuilder.WriteU32(buf, 0x00, 0);
            FormatTestBuilder.WriteU16(buf, 0x04, 0);

            var ex = FormatTestBuilder.ReadHeaderExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
            StringAssert.Contains("magic", ex.Message.ToLowerInvariant());
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ReadSectionTable
    // ══════════════════════════════════════════════════════════════════

    [TestFixture]
    public sealed class FormatReaderReadSectionTableTests
    {
        [Test]
        public void EmptyTable_ReturnsZeroCount()
        {
            var info = FormatTestBuilder.ReadTableInfo(FormatTestBuilder.NewMinimalValid());
            Assert.AreEqual(0, info.count);
            Assert.AreEqual(0, info.nameHeapLength);
        }

        [Test]
        public void OneEmptySection_ReturnsOneEntry()
        {
            var info = FormatTestBuilder.ReadTableInfo(
                FormatTestBuilder.NewWithOneEmptySection());
            Assert.AreEqual(1, info.count);
        }

        [Test]
        public void TableOffsetNegative_Throws()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 0x18, ulong.MaxValue);
            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void TableEndExceedsView_Throws()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 0x18, 104UL - 10);
            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void EntryReserved_NonZero_Throws()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU32(buf, 48 + 44, 1);
            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void EntryNameLength_OverMax_Throws()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU16(buf, 48 + 6,
                (ushort)(ECSaveFormatConstants.MaxNameLength + 1));
            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void EntryOffsetPlusLengthOverTotal_Throws()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 48 + 24, 110);
            FormatTestBuilder.WriteU64(buf, 48 + 32, 24);
            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void EntryOffsetPlusLength_Overflow_Throws()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 48 + 24, ulong.MaxValue);
            FormatTestBuilder.WriteU64(buf, 48 + 32, 1);
            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void NameHeapOutOfRange_Throws()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 0x18, 100);
            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void EntryValues_Preserved()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU32(buf, 48 + 0, 0xCAFE);
            FormatTestBuilder.WriteU8(buf, 48 + 4, (byte)SectionKind.Identity);
            FormatTestBuilder.WriteU8(buf, 48 + 5, 0x01);
            FormatTestBuilder.WriteU64(buf, 48 + 8, 0xA1B2C3D4UL);
            FormatTestBuilder.WriteU64(buf, 48 + 16, 0xDEADBEEFUL);
            FormatTestBuilder.WriteU64(buf, 48 + 24, 96);
            FormatTestBuilder.WriteU64(buf, 48 + 32, 0);
            FormatTestBuilder.WriteU32(buf, 48 + 40, 0x0102);

            var e = FormatTestBuilder.ReadEntry(buf, 0);
            Assert.AreEqual(0xCAFEu, e.SectionId);
            Assert.AreEqual(SectionKind.Identity, e.Kind);
            Assert.AreEqual(0x01, e.Flags);
            Assert.AreEqual(0xA1B2C3D4UL, e.NameHash);
            Assert.AreEqual(0xDEADBEEFUL, e.SchemaHash);
            Assert.AreEqual(96UL, e.Offset);
            Assert.AreEqual(0UL, e.Length);
            Assert.AreEqual(0x0102u, e.NameOffset);
            Assert.IsTrue(e.HasTypeDescriptor);
        }

        [Test]
        public void EntryKind_UnknownValue_Accepted()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU8(buf, 48 + 4, 0xFF);
            var info = FormatTestBuilder.ReadTableInfo(buf);
            Assert.AreEqual(1, info.count);
        }

        [Test]
        public void ErrorPrecedence_ReservedBeforeNameLength()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU32(buf, 48 + 44, 1);
            FormatTestBuilder.WriteU16(buf, 48 + 6,
                (ushort)(ECSaveFormatConstants.MaxNameLength + 1));

            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
            StringAssert.Contains("reserved", ex.Message.ToLowerInvariant());
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ReadTypeDescriptor
    // ══════════════════════════════════════════════════════════════════

    [TestFixture]
    public sealed class FormatReaderReadTypeDescriptorTests
    {
        [Test]
        public void ValidBlock_ReturnsBlock()
        {
            var block = FormatTestBuilder.ReadDescriptor(
                FormatTestBuilder.NewWithTypeDescriptor(), 0);
            Assert.AreEqual(1, block.EntryCount);
            Assert.AreEqual(0, block.RawEntryCount);
            Assert.AreEqual(2, block.SchemaHashInputLength);
            Assert.AreEqual(2, block.NameHeapLength);
        }

        [Test]
        public void NegativeIndex_Throws()
        {
            var ex = FormatTestBuilder.ReadDescriptorExpectError(
                FormatTestBuilder.NewWithTypeDescriptor(), -1);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void IndexAtCount_Throws()
        {
            var ex = FormatTestBuilder.ReadDescriptorExpectError(
                FormatTestBuilder.NewWithTypeDescriptor(), 1);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void NoTypeDescriptorFlag_Throws()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(flags: 0);
            var ex = FormatTestBuilder.ReadDescriptorExpectError(buf, 0);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void BlockLenLessThan8_Throws()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(blockLen: 4);
            var ex = FormatTestBuilder.ReadDescriptorExpectError(buf, 0);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void BlockLenExceedsSectionLength_Throws()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(blockLen: 24);
            FormatTestBuilder.WriteU32(buf, 96, 100);
            var ex = FormatTestBuilder.ReadDescriptorExpectError(buf, 0);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void InputLenNegative_Throws()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(inputLen: 2);
            FormatTestBuilder.WriteI32(buf, 96 + 8, -1);
            var ex = FormatTestBuilder.ReadDescriptorExpectError(buf, 0);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void InputLenExceedsBlockLen_Throws()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(blockLen: 24, inputLen: 2);
            FormatTestBuilder.WriteI32(buf, 96 + 8, 30);
            var ex = FormatTestBuilder.ReadDescriptorExpectError(buf, 0);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void NameLenNegative_Throws()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(inputLen: 2);
            FormatTestBuilder.WriteI32(buf, 96 + 16, -1);
            var ex = FormatTestBuilder.ReadDescriptorExpectError(buf, 0);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void NameLenExceedsBlockLen_Throws()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(blockLen: 24, inputLen: 2);
            FormatTestBuilder.WriteI32(buf, 96 + 16, 30);
            var ex = FormatTestBuilder.ReadDescriptorExpectError(buf, 0);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void InputPreserved()
        {
            var input = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            var buf = FormatTestBuilder.NewWithTypeDescriptor(
                blockLen: 32, inputLen: 4, input: input);
            var block = FormatTestBuilder.ReadDescriptor(buf, 0);
            Assert.AreEqual(4, block.SchemaHashInputLength);
            CollectionAssert.AreEqual(input, block.SchemaHashInput);
        }

        [Test]
        public void NamesPreserved()
        {
            var names = new byte[] { 0x03, (byte)'A', (byte)'B', (byte)'C' };
            var buf = FormatTestBuilder.NewWithTypeDescriptor(
                blockLen: 32, inputLen: 2, nameLen: 4, names: names);
            var block = FormatTestBuilder.ReadDescriptor(buf, 0);
            Assert.AreEqual(4, block.NameHeapLength);
            CollectionAssert.AreEqual(names, block.NameHeap);
        }

        [Test]
        public void EntryCountPreserved()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(entryCount: 7);
            var block = FormatTestBuilder.ReadDescriptor(buf, 0);
            Assert.AreEqual(7, block.EntryCount);
        }

        [Test]
        public void RawEntryCountPreserved()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(rawEntryCount: 3);
            var block = FormatTestBuilder.ReadDescriptor(buf, 0);
            Assert.AreEqual(3, block.RawEntryCount);
        }

        [Test]
        public void EmptyInput_ZeroLength()
        {
            var buf = FormatTestBuilder.NewWithTypeDescriptor(
                blockLen: 24, inputLen: 0, input: new byte[0]);
            var block = FormatTestBuilder.ReadDescriptor(buf, 0);
            Assert.AreEqual(0, block.SchemaHashInputLength);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GetSectionRange
    // ══════════════════════════════════════════════════════════════════

    [TestFixture]
    public sealed class FormatReaderGetSectionRangeTests
    {
        [Test]
        public void ValidIndex_ReturnsRange()
        {
            var r = FormatTestBuilder.ReadRange(
                FormatTestBuilder.NewWithOneEmptySection(), 0);
            Assert.AreEqual(96UL, r.Offset);
            Assert.AreEqual(0UL, r.Length);
            Assert.AreEqual(SectionKind.Component, r.Kind);
        }

        [Test]
        public void NegativeIndex_Throws()
        {
            var ex = FormatTestBuilder.ReadRangeExpectError(
                FormatTestBuilder.NewWithOneEmptySection(), -1);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void IndexAtCount_Throws()
        {
            var ex = FormatTestBuilder.ReadRangeExpectError(
                FormatTestBuilder.NewWithOneEmptySection(), 1);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void MultipleSections_EachReturnsCorrectRange()
        {
            var buf = FormatTestBuilder.NewWithMultipleSections(new int[]
            {
                (int)SectionKind.Identity,
                (int)SectionKind.Component,
                (int)SectionKind.Buffer,
            });

            Assert.AreEqual(SectionKind.Identity, FormatTestBuilder.ReadRange(buf, 0).Kind);
            Assert.AreEqual(SectionKind.Component, FormatTestBuilder.ReadRange(buf, 1).Kind);
            Assert.AreEqual(SectionKind.Buffer, FormatTestBuilder.ReadRange(buf, 2).Kind);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Known limitations (documented)
    // ══════════════════════════════════════════════════════════════════

    [TestFixture]
    public sealed class FormatReaderAlignmentTests
    {
        [Test]
        public void UnalignedSectionTableOffset_Rejected()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 0x18, 49);

            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void UnalignedSectionOffset_Rejected()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 48 + 24, 97);

            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void SectionOverlapsHeader_Rejected()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 48 + 24, 8);

            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        [Test]
        public void SectionOverlapsFooter_Rejected()
        {
            var buf = FormatTestBuilder.NewWithOneEmptySection();
            FormatTestBuilder.WriteU64(buf, 48 + 32, 4);

            var ex = FormatTestBuilder.ReadTableExpectError(buf);
            Assert.AreEqual(FormatError.CorruptedData, ex.Error);
        }

        // ══════════════════════════════════════════════════════════════════
        //  NameHeap (merged from FormatReaderNameHeapTests)
        // ══════════════════════════════════════════════════════════════════

        [TestFixture]
        public sealed class FormatReaderNameHeapTests
        {
            [Test]
            public unsafe void NameHeap_StartsAfterSectionTable()
            {
                // Layout: header(48) + table(48) + NameHeap("A" at 96) + padding + empty section at 104
                var buf = new byte[256];
                fixed (byte* p = buf)
                {
                    const int headerSize = 0x30;    // 48
                    const int tableOffset = 0x30;   // 48
                    const int tableSize = 48;       // SectionEntrySize
                    const int nameHeapOffset = tableOffset + tableSize;  // 96

                    var w = new FormatWriter(p, buf.Length);
                    w.WriteHeader(new SaveHeader
                    {
                        Magic = ECSaveFormatConstants.Magic,
                        FormatVersion = 1,
                        HeaderFlags = 0,
                        HeaderSize = headerSize,
                        GameDataVersion = 1,
                        LogicalEntityCount = 0,
                        SectionCount = 1,
                        SectionTableOffset = tableOffset,
                        TotalLength = 256,
                        NameHeapLength = 1,
                        WorldIdLength = 0,
                        Reserved = 0,
                    });
                    w.Seek(tableOffset);
                    w.WriteSectionEntry(new SectionEntry
                    {
                        SectionId = 0,
                        Kind = SectionKind.Component,
                        Flags = 0,
                        NameLength = 1,
                        NameHash = 0,
                        SchemaHash = 0,
                        // Phase C-2: Offset は 8-byte アライン必須。
                        // NameHeapLength=1 のとき minSectionStart = AlignUp(96+1, 8) = 104。
                        // 空セクションなので Offset=104, Length=0 で valid。
                        Offset = 104,
                        Length = 0,
                        NameOffset = 0,
                        Reserved = 0,
                    });

                    p[nameHeapOffset] = (byte)'A';

                    var reader = new FormatReader(new ByteView(p, buf.Length));
                    reader.ReadHeader();
                    var table = reader.ReadSectionTable();

                    Assert.IsTrue(table.NameHeap != null,
                        "NameHeap pointer should not be null");
                    Assert.AreEqual(nameHeapOffset, (int)(table.NameHeap - p),
                        "NameHeap must start immediately after the section table");
                    Assert.AreEqual('A', (char)table.NameHeap[0]);
                }
            }
        }
    }
}