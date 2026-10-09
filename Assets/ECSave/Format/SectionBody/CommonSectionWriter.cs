namespace ECSave.Format
{
    public static unsafe class CommonSectionWriter
    {
        /// <summary>固定長セクションの全長（型記述子ブロックを含まない本体のみ）。</summary>
        public static int ComputeFixedBodySize(int count, int stride, bool hasEnabledBits)
        {
            long size = CommonSectionFormat.HeaderSize;
            size += (long)count * 4;                    // EntityIndex
            if (hasEnabledBits) size += (count + 7) / 8; // EnabledBits
            if (stride > 0) size += (long)count * stride; // Records
            if (size > int.MaxValue)
                throw new FormatException(FormatError.ValidationFailed, "section body too large");
            return (int)size;
        }

        /// <summary>可変長セクションのヘッダ部（Records 本体を除く）。</summary>
        public static int ComputeVariableHeaderSize(int count, bool hasEnabledBits)
        {
            long size = CommonSectionFormat.HeaderSize;
            size += (long)count * 4;                    // EntityIndex
            size += (long)(count + 1) * 4;              // RecordOffset
            if (hasEnabledBits) size += (count + 7) / 8;
            if (size > int.MaxValue)
                throw new FormatException(FormatError.ValidationFailed, "section header too large");
            return (int)size;
        }

        /// <summary>
        /// 固定長セクションを書く。事前に ComputeFixedBodySize でバッファを確保しておくこと。
        /// </summary>
        public static void WriteFixed(
            ref FormatWriter w,
            uint count,
            uint stride,
            uint* entityIndex,      // count 個（count>0 なら非 null）
            byte* enabledBits,      // null なら書かない
            byte* records)          // count×stride バイト（count>0 かつ stride>0 なら非 null）
        {
            uint flags = ECSaveFormatConstants.SectionFlags_FixedLength;
            if (enabledBits != null) flags |= ECSaveFormatConstants.SectionFlags_HasEnabledBits;

            w.WriteU32(count);
            w.WriteU32(flags);
            w.WriteU32(stride);

            if (count > 0)
                w.WriteBytes((byte*)entityIndex, (int)count * 4);

            if (enabledBits != null)
            {
                int eb = ((int)count + 7) / 8;
                if (eb > 0) w.WriteBytes(enabledBits, eb);
            }

            if (stride > 0 && count > 0)
                w.WriteBytes(records, (int)((long)count * stride));
        }

        /// <summary>
        /// 可変長セクションを書く。recordOffset は count+1 個、recordOffset[count] = recordsLength。
        /// </summary>
        public static void WriteVariable(
            ref FormatWriter w,
            uint count,
            uint stride,
            uint* entityIndex,
            uint* recordOffset,
            byte* enabledBits,
            byte* records,
            int recordsLength)
        {
            uint flags = 0;
            if (enabledBits != null) flags |= ECSaveFormatConstants.SectionFlags_HasEnabledBits;

            w.WriteU32(count);
            w.WriteU32(flags);
            w.WriteU32(stride);

            if (count > 0)
                w.WriteBytes((byte*)entityIndex, (int)count * 4);

            w.WriteBytes((byte*)recordOffset, (int)((count + 1) * 4));

            if (enabledBits != null)
            {
                int eb = ((int)count + 7) / 8;
                if (eb > 0) w.WriteBytes(enabledBits, eb);
            }

            if (recordsLength > 0)
                w.WriteBytes(records, recordsLength);
        }
    }
}
