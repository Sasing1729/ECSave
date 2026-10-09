namespace ECSave.Format
{
    /// <summary>共通形式セクションの読み取り結果（内部設計 6.4）。</summary>
    public unsafe struct CommonSectionView
    {
        public CommonSectionHeader Header;
        public uint* EntityIndex;      // Count 個
        public uint* RecordOffset;     // 可変長時のみ非 null、Count+1 個
        public byte* EnabledBits;      // HasEnabledBits 時のみ非 null
        public byte* Records;          // 固定長: Count × Stride、可変長: RecordOffset[Count] バイト
        public int RecordsLength;
    }

    public static unsafe class CommonSectionReader
    {
        /// <summary>セクション本体全体（型記述子ブロックを除く）を読む。</summary>
        public static CommonSectionView Read(ByteView view)
        {
            var r = new ByteReader(view);
            var h = CommonSectionFormat.ReadHeader(ref r);
            CommonSectionFormat.ValidateFlags(h.Flags, h.Count);

            int count = (int)h.Count;
            bool isFixed = h.IsFixedLength;
            bool hasEnabled = h.HasEnabledBits;

            if (count > 0 && (ulong)count * 4 > int.MaxValue)
                throw new FormatException(FormatError.CorruptedData, "Count×4 overflow");

            // 1. EntityIndex
            if (!view.BoundsCheck(r.Offset, count * 4))
                throw new FormatException(FormatError.CorruptedData, "EntityIndex out of range");
            uint* entityIndex = count > 0 ? (uint*)r.ReadBytesPtr(count * 4) : null;

            // 2. RecordOffset（可変長時）
            uint* recordOffset = null;
            if (!isFixed)
            {
                int bytes = checked((count + 1) * 4);
                if (!view.BoundsCheck(r.Offset, bytes))
                    throw new FormatException(FormatError.CorruptedData, "RecordOffset out of range");
                recordOffset = (uint*)r.ReadBytesPtr(bytes);
            }

            // 3. EnabledBits
            byte* enabledBits = null;
            if (hasEnabled)
            {
                int bytes = (count + 7) / 8;
                if (bytes > 0)
                {
                    if (!view.BoundsCheck(r.Offset, bytes))
                        throw new FormatException(FormatError.CorruptedData, "EnabledBits out of range");
                    enabledBits = r.ReadBytesPtr(bytes);
                }
            }

            // 4. Records
            int recordsLength = view.Length - r.Offset;
            byte* records = recordsLength > 0 ? view.Pointer + r.Offset : null;

            if (isFixed)
            {
                long need = (long)count * h.Stride;
                if (need != recordsLength)
                    throw new FormatException(FormatError.CorruptedData,
                        $"fixed section records length mismatch: expected {need}, got {recordsLength}");
            }
            else
            {
                ValidateRecordOffset(recordOffset, count, recordsLength);
            }

            return new CommonSectionView
            {
                Header = h,
                EntityIndex = entityIndex,
                RecordOffset = recordOffset,
                EnabledBits = enabledBits,
                Records = records,
                RecordsLength = recordsLength,
            };
        }

        private static unsafe void ValidateRecordOffset(uint* offsets, int count, int available)
        {
            if (offsets[0] != 0)
                throw new FormatException(FormatError.CorruptedData, "RecordOffset[0] must be 0");
            uint prev = 0;
            for (int i = 1; i <= count; i++)
            {
                if (offsets[i] < prev)
                    throw new FormatException(FormatError.CorruptedData, "RecordOffset not monotonic");
                if (offsets[i] > (uint)available)
                    throw new FormatException(FormatError.CorruptedData, "RecordOffset exceeds available bytes");
                prev = offsets[i];
            }
        }
    }
}
