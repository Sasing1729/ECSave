// Runtime/Format/FormatReader.cs
using System;

namespace ECSave.Format
{
    /// <summary>
    /// SaveData を読み取る（内部設計 6章）。すべての読み取りは ByteView 経由。
    /// </summary>
    public unsafe struct FormatReader
    {
        private ByteView _view;
        private SaveHeader _header;

        public FormatReader(ByteView view)
        {
            _view = view;
            _header = default;
        }

        public SaveHeader Header => _header;
        public ByteView View => _view;

        /// <summary>ヘッダを読む。範囲・Magic・予約ビットを検証する（6.2、6.9）。</summary>
        public SaveHeader ReadHeader()
        {
            if (_view.Length < ECSaveFormatConstants.HeaderFixedSize + ECSaveFormatConstants.FooterSize)
                throw new FormatException(FormatError.CorruptedData, "input too small for header + footer");

            var r = new ByteReader(_view);
            var h = new SaveHeader
            {
                Magic = r.ReadU32(),
                FormatVersion = r.ReadU16(),
                HeaderFlags = r.ReadU16(),
                HeaderSize = r.ReadU32(),
                GameDataVersion = r.ReadI32(),
                LogicalEntityCount = r.ReadI32(),
                SectionCount = r.ReadI32(),
                SectionTableOffset = r.ReadU64(),
                TotalLength = r.ReadU64(),
                NameHeapLength = r.ReadU32(),
                WorldIdLength = r.ReadU16(),
                Reserved = r.ReadU16(),
            };

            if (h.Magic != ECSaveFormatConstants.Magic)
                throw new FormatException(FormatError.CorruptedData, $"magic mismatch: 0x{h.Magic:X8}");

            if (h.HeaderFlags != 0)
                throw new FormatException(FormatError.CorruptedData, $"HeaderFlags must be 0, got 0x{h.HeaderFlags:X4}");

            if (h.Reserved != 0)
                throw new FormatException(FormatError.CorruptedData, "header reserved must be 0");

            if (h.FormatVersion == 0 || h.FormatVersion > ECSaveFormatConstants.CurrentFormatVersion)
                throw new FormatException(FormatError.VersionMismatch, $"FormatVersion {h.FormatVersion} > current {ECSaveFormatConstants.CurrentFormatVersion}");

            if (h.LogicalEntityCount < 0 || h.LogicalEntityCount > ECSaveFormatConstants.MaxLogicalEntityCount)
                throw new FormatException(FormatError.CorruptedData, $"LogicalEntityCount out of range: {h.LogicalEntityCount}");

            if (h.SectionCount < 0 || h.SectionCount > ECSaveFormatConstants.MaxSectionCount)
                throw new FormatException(FormatError.CorruptedData, $"SectionCount out of range: {h.SectionCount}");

            if (h.WorldIdLength > ECSaveFormatConstants.MaxWorldIdLength)
                throw new FormatException(FormatError.CorruptedData, $"WorldIdLength out of range: {h.WorldIdLength}");

            if (h.TotalLength != (ulong)_view.Length)
                throw new FormatException(FormatError.CorruptedData, $"TotalLength mismatch: {h.TotalLength} vs actual {_view.Length}");

            // WorldId は HeaderSize 直後
            long worldIdStart = (long)ECSaveFormatConstants.HeaderFixedSize;
            long worldIdEnd = worldIdStart + h.WorldIdLength;
            if (worldIdEnd > _view.Length)
                throw new FormatException(FormatError.CorruptedData, "WorldId out of range");

            h.WorldIdPtr = _view.Pointer + worldIdStart;

            if (h.HeaderSize < worldIdEnd)
                throw new FormatException(FormatError.CorruptedData, $"HeaderSize {h.HeaderSize} < minimum {worldIdEnd}");

            _header = h;
            return h;
        }

        /// <summary>セクション表を読む（6.3）。</summary>
        public SectionTable ReadSectionTable()
        {
            int count = _header.SectionCount;
            long tableStart = (long)_header.SectionTableOffset;
            long tableEnd = tableStart + (long)count * ECSaveFormatConstants.SectionEntrySize;
            if (tableStart < 0 || tableEnd > _view.Length)
                throw new FormatException(FormatError.CorruptedData, "section table out of range");

            var entries = (SectionEntry*)(_view.Pointer + tableStart);

            for (int i = 0; i < count; i++)
            {
                ref var e = ref entries[i];
                if (e.Reserved != 0)
                    throw new FormatException(FormatError.CorruptedData, $"section entry {i} reserved must be 0");
                if (e.NameLength > ECSaveFormatConstants.MaxNameLength)
                    throw new FormatException(FormatError.CorruptedData, $"section entry {i} NameLength too large");
                if (e.Offset + e.Length > _header.TotalLength || e.Offset + e.Length < e.Offset)
                    throw new FormatException(FormatError.CorruptedData, $"section entry {i} range out of file");
                if (e.Kind != SectionKind.Unknown && (byte)e.Kind > 5)
                    throw new FormatException(FormatError.CorruptedData, $"section entry {i} unknown kind {(byte)e.Kind}");
            }

            long nameHeapStart = tableEnd;
            if (nameHeapStart < 0 || nameHeapStart > _view.Length)
                throw new FormatException(FormatError.CorruptedData, "name heap out of range");

            return new SectionTable
            {
                Entries = entries,
                Count = count,
                NameHeap = _view.Pointer + nameHeapStart,
                NameHeapLength = (int)_header.NameHeapLength,
            };
        }

        /// <summary>型記述子ブロックを読む（6.10）。</summary>
        public TypeDescriptorBlock ReadTypeDescriptor(int sectionIndex)
        {
            var table = ReadSectionTable();
            if (sectionIndex < 0 || sectionIndex >= table.Count)
                throw new FormatException(FormatError.CorruptedData, "section index out of range");

            ref var e = ref table.Entries[sectionIndex];
            if (!e.HasTypeDescriptor)
                throw new FormatException(FormatError.CorruptedData, $"section {sectionIndex} has no type descriptor");

            long secStart = (long)e.Offset;
            if (secStart + 4 > _view.Length)
                throw new FormatException(FormatError.CorruptedData, "type descriptor length out of range");

            var r = new ByteReader(new ByteView(_view.Pointer + secStart, (int)e.Length));
            uint blockLen = r.ReadU32();
            if (blockLen < 8 || blockLen > e.Length)
                throw new FormatException(FormatError.CorruptedData, $"type descriptor block length {blockLen} invalid");

            ushort entryCount = r.ReadU16();
            ushort rawEntryCount = r.ReadU16();

            int inputLen = r.ReadI32();
            if (inputLen < 0 || inputLen > blockLen)
                throw new FormatException(FormatError.CorruptedData, "schema hash input length invalid");
            byte* inputPtr = r.ReadBytesPtr(inputLen);

            r.AlignTo(ECSaveFormatConstants.TypeDescriptorAlignment);

            int nameLen = r.ReadI32();
            if (nameLen < 0 || nameLen > blockLen)
                throw new FormatException(FormatError.CorruptedData, "field name heap length invalid");
            byte* namePtr = r.ReadBytesPtr(nameLen);

            return new TypeDescriptorBlock
            {
                EntryCount = entryCount,
                RawEntryCount = rawEntryCount,
                SchemaHashInput = inputPtr,
                SchemaHashInputLength = inputLen,
                NameHeap = namePtr,
                NameHeapLength = nameLen,
            };
        }

        /// <summary>セクション本体の位置と長さを返す（共通形式の前処理は各層が行う）。</summary>
        public void GetSectionRange(int sectionIndex, out ulong offset, out ulong length, out SectionKind kind)
        {
            var table = ReadSectionTable();
            if (sectionIndex < 0 || sectionIndex >= table.Count)
                throw new FormatException(FormatError.CorruptedData, "section index out of range");
            ref var e = ref table.Entries[sectionIndex];
            offset = e.Offset;
            length = e.Length;
            kind = e.Kind;
        }
    }
}
