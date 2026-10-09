namespace ECSave.Format
{
    public static unsafe class ChunkComponentSectionWriter
    {
        /// <summary>固定長セクションの全長（型記述子ブロックを除く本体）。</summary>
        public static int ComputeFixedBodySize(int groupCount, int memberCount, int stride)
        {
            long size = ChunkComponentSectionFormat.HeaderSize;
            size += (long)(groupCount + 1) * 4; // MemberOffset
            size += (long)memberCount * 4;      // MemberEntityIndex
            if (stride > 0) size += (long)groupCount * stride; // Records
            if (size > int.MaxValue)
                throw new FormatException(FormatError.ValidationFailed, "chunk section too large");
            return (int)size;
        }

        /// <summary>可変長セクションのヘッダ部（Records 本体を除く）。</summary>
        public static int ComputeVariableHeaderSize(int groupCount, int memberCount)
        {
            long size = ChunkComponentSectionFormat.HeaderSize;
            size += (long)(groupCount + 1) * 4; // MemberOffset
            size += (long)memberCount * 4;      // MemberEntityIndex
            size += (long)(groupCount + 1) * 4; // RecordOffset
            if (size > int.MaxValue)
                throw new FormatException(FormatError.ValidationFailed, "chunk header too large");
            return (int)size;
        }

        public static void WriteFixed(
            ref FormatWriter w,
            uint groupCount,
            uint stride,
            uint* memberOffset,        // groupCount+1 個
            int memberCount,
            uint* memberEntityIndex,   // memberCount 個
            byte* records)             // groupCount × stride バイト
        {
            w.WriteU32(groupCount);
            w.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);
            w.WriteU32(stride);

            w.WriteBytes((byte*)memberOffset, (int)((groupCount + 1) * 4));
            if (memberCount > 0)
                w.WriteBytes((byte*)memberEntityIndex, memberCount * 4);

            if (stride > 0 && groupCount > 0)
                w.WriteBytes(records, (int)((long)groupCount * stride));
        }

        public static void WriteVariable(
            ref FormatWriter w,
            uint groupCount,
            uint stride,
            uint* memberOffset,
            int memberCount,
            uint* memberEntityIndex,
            uint* recordOffset,        // groupCount+1 個
            byte* records,
            int recordsLength)
        {
            w.WriteU32(groupCount);
            w.WriteU32(0);
            w.WriteU32(stride);

            w.WriteBytes((byte*)memberOffset, (int)((groupCount + 1) * 4));
            if (memberCount > 0)
                w.WriteBytes((byte*)memberEntityIndex, memberCount * 4);
            w.WriteBytes((byte*)recordOffset, (int)((groupCount + 1) * 4));

            if (recordsLength > 0)
                w.WriteBytes(records, recordsLength);
        }
    }
}
