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

        /// <summary>
        /// 固定長セクションのヘッダ部（GroupCount / Flags / Stride + MemberOffset + MemberEntityIndex）を書く。
        /// Records 本体は呼び出し側が個別に書く（EntityRefCodec を経由するため）。
        /// </summary>
        /// <param name="w">書き込み先</param>
        /// <param name="groupCount">グループ数（= チャンク数）</param>
        /// <param name="dataStride">
        /// レコードのストライド。EntityRef を含む型では <c>rawStride - 4 * refs.Length</c>。
        /// ヘッダの Stride フィールドに書かれる。
        /// </param>
        /// <param name="memberOffset">groupCount+1 個の uint。MemberOffset[0] は必ず 0。</param>
        /// <param name="memberCount">MemberEntityIndex の要素数。</param>
        /// <param name="memberEntityIndex">memberCount 個の uint（pid）。</param>
        public static void WriteFixedHeader(
            ref FormatWriter w,
            uint groupCount,
            uint dataStride,
            uint* memberOffset,
            int memberCount,
            uint* memberEntityIndex)
        {
            w.WriteU32(groupCount);
            w.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);
            w.WriteU32(dataStride);

            w.WriteBytes((byte*)memberOffset, (int)((groupCount + 1) * 4));
            if (memberCount > 0)
                w.WriteBytes((byte*)memberEntityIndex, memberCount * 4);
        }

        /// <summary>
        /// 可変長セクションのヘッダ部を書く。Records 本体は呼び出し側が個別に書く。
        /// </summary>
        public static void WriteVariableHeader(
            ref FormatWriter w,
            uint groupCount,
            uint dataStride,
            uint* memberOffset,
            int memberCount,
            uint* memberEntityIndex,
            uint* recordOffset)
        {
            w.WriteU32(groupCount);
            w.WriteU32(0);
            w.WriteU32(dataStride);

            w.WriteBytes((byte*)memberOffset, (int)((groupCount + 1) * 4));
            if (memberCount > 0)
                w.WriteBytes((byte*)memberEntityIndex, memberCount * 4);
            w.WriteBytes((byte*)recordOffset, (int)((groupCount + 1) * 4));
        }

        /// <summary>
        /// 固定長セクションを書く便宜ラッパー。
        /// <para>
        /// EntityRef を含まない単純なセクション（テストフィクスチャ等）向け。
        /// EntityRef を含む場合は <see cref="WriteFixedHeader"/> と
        /// <see cref="EntityRefCodec.WriteRecordWithRefs"/> を組み合わせて使うこと。
        /// </para>
        /// <para>
        /// 内部で <see cref="WriteFixedHeader"/> を呼び、その後 records を生バイトで書く。
        /// </para>
        /// </summary>
        public static void WriteFixed(
            ref FormatWriter w,
            uint groupCount,
            uint stride,
            uint* memberOffset,
            int memberCount,
            uint* memberEntityIndex,
            byte* records)
        {
            WriteFixedHeader(ref w, groupCount, stride, memberOffset,
                memberCount, memberEntityIndex);

            if (stride > 0 && groupCount > 0)
                w.WriteBytes(records, (int)((long)groupCount * stride));
        }

        /// <summary>
        /// 可変長セクションを書く便宜ラッパー。
        /// <para>
        /// EntityRef を含まない単純なセクション（テストフィクスチャ等）向け。
        /// EntityRef を含む場合は <see cref="WriteVariableHeader"/> と
        /// <see cref="EntityRefCodec.WriteRecordWithRefs"/> を組み合わせて使うこと。
        /// </para>
        /// </summary>
        public static void WriteVariable(
            ref FormatWriter w,
            uint groupCount,
            uint stride,
            uint* memberOffset,
            int memberCount,
            uint* memberEntityIndex,
            uint* recordOffset,
            byte* records,
            int recordsLength)
        {
            WriteVariableHeader(ref w, groupCount, stride, memberOffset,
                memberCount, memberEntityIndex, recordOffset);

            if (recordsLength > 0)
                w.WriteBytes(records, recordsLength);
        }
    }
}
