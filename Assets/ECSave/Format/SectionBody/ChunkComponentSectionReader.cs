namespace ECSave.Format
{
    /// <summary>ChunkComponent セクションの読み取り結果（6.4）。</summary>
    public unsafe struct ChunkComponentSectionView
    {
        public ChunkComponentSectionHeader Header;
        public uint* MemberOffset;      // GroupCount+1 個（厳密単調増加、末尾＝メンバー総数）
        public uint* MemberEntityIndex; // MemberOffset[GroupCount] 個
        public uint* RecordOffset;      // 可変長時のみ、GroupCount+1 個
        public byte* Records;           // 固定長: GroupCount × Stride、可変長: RecordOffset[GroupCount] バイト
        public int RecordsLength;
    }

    public static unsafe class ChunkComponentSectionReader
    {
        public static ChunkComponentSectionView Read(ByteView view)
        {
            var r = new ByteReader(view);
            var h = ChunkComponentSectionFormat.ReadHeader(ref r);
            ChunkComponentSectionFormat.ValidateFlags(h.Flags);

            if (h.GroupCount == 0)
                throw new FormatException(FormatError.CorruptedData, "GroupCount must be >= 1");
            if (h.GroupCount > ECSaveFormatConstants.MaxLogicalEntityCount)
                throw new FormatException(FormatError.CorruptedData, $"GroupCount out of range: {h.GroupCount}");

            int groupCount = (int)h.GroupCount;
            bool isFixed = h.IsFixedLength;

            // 1. MemberOffset (GroupCount+1)
            int moBytes = checked((groupCount + 1) * 4);
            if (!view.BoundsCheck(r.Offset, moBytes))
                throw new FormatException(FormatError.CorruptedData, "MemberOffset out of range");
            uint* memberOffset = (uint*)r.ReadBytesPtr(moBytes);
            ValidateMemberOffset(memberOffset, groupCount);

            // 2. MemberEntityIndex (memberCount)
            uint memberCount = memberOffset[groupCount];
            if ((ulong)memberCount * 4 > int.MaxValue)
                throw new FormatException(FormatError.CorruptedData, "MemberEntityIndex too large");
            int meiBytes = (int)memberCount * 4;
            if (!view.BoundsCheck(r.Offset, meiBytes))
                throw new FormatException(FormatError.CorruptedData, "MemberEntityIndex out of range");
            uint* memberEntityIndex = meiBytes > 0 ? (uint*)r.ReadBytesPtr(meiBytes) : null;

            // 3. RecordOffset（可変長時）
            uint* recordOffset = null;
            if (!isFixed)
            {
                int roBytes = checked((groupCount + 1) * 4);
                if (!view.BoundsCheck(r.Offset, roBytes))
                    throw new FormatException(FormatError.CorruptedData, "RecordOffset out of range");
                recordOffset = (uint*)r.ReadBytesPtr(roBytes);
            }

            // 4. Records
            int recordsLength = view.Length - r.Offset;
            byte* records = recordsLength > 0 ? view.Pointer + r.Offset : null;

            if (isFixed)
            {
                long need = (long)groupCount * h.Stride;
                if (need != recordsLength)
                    throw new FormatException(FormatError.CorruptedData,
                        $"chunk fixed records mismatch: expected {need}, got {recordsLength}");
            }
            else
            {
                ValidateRecordOffset(recordOffset, groupCount, recordsLength);
            }

            return new ChunkComponentSectionView
            {
                Header = h,
                MemberOffset = memberOffset,
                MemberEntityIndex = memberEntityIndex,
                RecordOffset = recordOffset,
                Records = records,
                RecordsLength = recordsLength,
            };
        }

        private static unsafe void ValidateMemberOffset(uint* offsets, int groupCount)
        {
            if (offsets[0] != 0)
                throw new FormatException(FormatError.CorruptedData, "MemberOffset[0] must be 0");
            for (int i = 1; i <= groupCount; i++)
            {
                if (offsets[i] <= offsets[i - 1])
                    throw new FormatException(FormatError.CorruptedData,
                        $"MemberOffset not strictly increasing at [{i}]");
            }
        }

        private static unsafe void ValidateRecordOffset(uint* offsets, int groupCount, int available)
        {
            if (offsets[0] != 0)
                throw new FormatException(FormatError.CorruptedData, "RecordOffset[0] must be 0");
            for (int i = 1; i <= groupCount; i++)
            {
                if (offsets[i] < offsets[i - 1])
                    throw new FormatException(FormatError.CorruptedData, "RecordOffset not monotonic");
                if (offsets[i] > (uint)available)
                    throw new FormatException(FormatError.CorruptedData, "RecordOffset exceeds available");
            }
        }
    }
}
