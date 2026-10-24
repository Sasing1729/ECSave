using System;

namespace ECSave.Format
{
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

        public SectionTable ReadSectionTable()
        {
            int count = _header.SectionCount;
            long tableStart = (long)_header.SectionTableOffset;
            long tableEnd = tableStart + (long)count * ECSaveFormatConstants.SectionEntrySize;
            if (tableStart < 0 || tableEnd > _view.Length)
                throw new FormatException(FormatError.CorruptedData, "section table out of range");

            // Review #12a: SectionTableOffset は 8-byte アライン必須。
            // (SectionEntry*) の未整列読み取りは ARM で危険。
            if ((_header.SectionTableOffset & 7UL) != 0)
                throw new FormatException(FormatError.CorruptedData,
                    $"SectionTableOffset must be 8-aligned: {_header.SectionTableOffset}");

            long nameHeapStart = tableEnd;
            if (nameHeapStart < 0 || nameHeapStart > _view.Length)
                throw new FormatException(FormatError.CorruptedData, "name heap out of range");

            // Review #12b: セクション領域の最小開始位置（header + table + nameHeap）。
            long nameHeapEnd = nameHeapStart + (long)_header.NameHeapLength;
            long minSectionStart = (nameHeapEnd + 7L) & ~7L;

            // Review #12b: セクション領域の最大終端（footer 直前）。
            long maxSectionEnd = (long)_header.TotalLength - ECSaveFormatConstants.FooterSize;

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

                // Review #12a: 8-byte アライン
                if ((e.Offset & 7UL) != 0)
                    throw new FormatException(FormatError.CorruptedData,
                        $"section entry {i} Offset must be 8-aligned: {e.Offset}");

                // Review #12b: header/table/nameHeap との重なり
                if ((long)e.Offset < minSectionStart)
                    throw new FormatException(FormatError.CorruptedData,
                        $"section entry {i} overlaps header/table/nameHeap: offset={e.Offset}, min={minSectionStart}");

                // Review #12b: footer との重なり
                long sectionEnd = (long)e.Offset + (long)e.Length;
                if (sectionEnd > maxSectionEnd)
                    throw new FormatException(FormatError.CorruptedData,
                        $"section entry {i} overlaps footer: end={sectionEnd}, max={maxSectionEnd}");

                // Kind の値域検査は行わない（設計 6.3）。
            }

            return new SectionTable
            {
                Entries = entries,
                Count = count,
                NameHeap = _view.Pointer + nameHeapStart,
                NameHeapLength = (int)_header.NameHeapLength,
            };
        }

        public TypeDescriptorBlock ReadTypeDescriptor(int sectionIndex)
        {
            var table = ReadSectionTable();
            if (sectionIndex < 0 || sectionIndex >= table.Count)
                throw new FormatException(FormatError.CorruptedData, "section index out of range");

            ref var e = ref table.Entries[sectionIndex];
            if (!e.HasTypeDescriptor)
                throw new FormatException(FormatError.CorruptedData, $"section {sectionIndex} has no type descriptor");

            if (e.Offset > int.MaxValue)
                throw new FormatException(FormatError.CorruptedData,
                    $"section {sectionIndex} Offset exceeds int.MaxValue: {e.Offset}");
            if (e.Length > int.MaxValue)
                throw new FormatException(FormatError.CorruptedData,
                    $"section {sectionIndex} Length exceeds int.MaxValue: {e.Length}");

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
