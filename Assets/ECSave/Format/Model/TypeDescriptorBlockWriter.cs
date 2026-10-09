using System.Text;

namespace ECSave.Format
{
    /// <summary>
    /// 型記述子ブロックの書き込み（内部設計 6.10）。
    /// ブロック構造：
    ///   u32 blockLength（全体長）
    ///   u16 EntryCount
    ///   u16 RawEntryCount
    ///   i32 schemaHashInputLength
    ///   byte[schemaHashInputLength] schemaHashInput（5.4節）
    ///   [8B align]
    ///   i32 nameHeapLength
    ///   entryCount 個の (u8 nameLength, byte[nameLength] nameUtf8) ※ A.B 形式、255 以下
    ///   [8B align]
    /// </summary>
    public static unsafe class TypeDescriptorBlockWriter
    {
        /// <summary>書く前にバッファ長を確定する。</summary>
        public static int ComputeSize(
            int schemaHashInputLength,
            int entryCount,
            int fieldNamesUtf8TotalLength /* 各名前の u8 length を除いた UTF-8 バイト数の合計 */)
        {
            if (schemaHashInputLength < 0) throw new FormatException(FormatError.ValidationFailed, "negative input length");
            if (entryCount < 0 || entryCount > ushort.MaxValue) throw new FormatException(FormatError.ValidationFailed, "entryCount out of range");
            if (fieldNamesUtf8TotalLength < 0) throw new FormatException(FormatError.ValidationFailed, "negative names length");

            long size = 4;                                  // block length u32
            size += 2 + 2;                                  // EntryCount + RawEntryCount
            size += 4 + schemaHashInputLength;              // input length + input
            size = AlignUp(size, ECSaveFormatConstants.TypeDescriptorAlignment);
            size += 4;                                      // nameHeapLength u32
            size += (long)entryCount;                       // 各名前の u8 length
            size += fieldNamesUtf8TotalLength;              // UTF-8 本体
            size = AlignUp(size, ECSaveFormatConstants.TypeDescriptorAlignment);

            if (size > int.MaxValue)
                throw new FormatException(FormatError.ValidationFailed, "type descriptor block too large");
            return (int)size;
        }

        /// <summary>
        /// ブロックを書く。バッファは ComputeSize で確保しておくこと。
        /// </summary>
        /// <param name="w">書き込み先</param>
        /// <param name="entryCount">フィールドエントリ数（ネスト展開後、Raw を除く）</param>
        /// <param name="rawEntryCount">Raw エントリ数</param>
        /// <param name="schemaHashInput">SchemaHash 入力列（5.4節）</param>
        /// <param name="schemaHashInputLength">入力列の長さ</param>
        /// <param name="fieldNames">フィールド名（EntryCount 個、入力列のエントリ順と一致）</param>
        /// <returns>書いたブロック全体長（アラインメント含む）</returns>
        public static int Write(
            ref FormatWriter w,
            ushort entryCount,
            ushort rawEntryCount,
            byte* schemaHashInput, int schemaHashInputLength,
            string[] fieldNames)
        {
            if (fieldNames == null || fieldNames.Length != entryCount)
                throw new FormatException(FormatError.ValidationFailed,
                    $"fieldNames.Length {(fieldNames?.Length ?? 0)} != EntryCount {entryCount}");

            int start = w.Offset;

            // 名前の UTF-8 バイト列を事前計算
            int n = fieldNames.Length;
            byte[][] nameBytes = new byte[n][];
            long nameBytesTotal = 0;
            for (int i = 0; i < n; i++)
            {
                string name = fieldNames[i] ?? string.Empty;
                byte[] bytes = Encoding.UTF8.GetBytes(name);
                if (bytes.Length > ECSaveFormatConstants.MaxFieldNameLength)
                    throw new FormatException(FormatError.ValidationFailed,
                        $"field name '{name}' exceeds {ECSaveFormatConstants.MaxFieldNameLength} UTF-8 bytes");
                nameBytes[i] = bytes;
                nameBytesTotal += 1 + bytes.Length; // u8 長さ + 本体
            }

            // ブロック長を後でパッチ
            w.WriteU32(0);
            w.WriteU16(entryCount);
            w.WriteU16(rawEntryCount);

            w.WriteI32(schemaHashInputLength);
            if (schemaHashInputLength > 0)
            {
                if (schemaHashInput == null)
                    throw new FormatException(FormatError.ValidationFailed, "schemaHashInput is null");
                w.WriteBytes(schemaHashInput, schemaHashInputLength);
            }

            w.AlignTo(ECSaveFormatConstants.TypeDescriptorAlignment);

            // 名前列
            if (nameBytesTotal > int.MaxValue)
                throw new FormatException(FormatError.ValidationFailed, "name heap too large");
            w.WriteI32((int)nameBytesTotal);
            for (int i = 0; i < n; i++)
            {
                byte[] bytes = nameBytes[i];
                w.WriteU8((byte)bytes.Length);
                if (bytes.Length > 0)
                {
                    fixed (byte* p = bytes)
                        w.WriteBytes(p, bytes.Length);
                }
            }

            w.AlignTo(ECSaveFormatConstants.TypeDescriptorAlignment);

            int totalLen = w.Offset - start;

            // ブロック長パッチ
            int end = w.Offset;
            w.Seek(start);
            w.WriteU32((uint)totalLen);
            w.Seek(end);

            return totalLen;
        }

        private static long AlignUp(long value, int alignment)
        {
            long rem = value % alignment;
            return rem == 0 ? value : value + (alignment - rem);
        }
    }
}
