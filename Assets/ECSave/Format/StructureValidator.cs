// Runtime/Format/StructureValidator.cs
using System;

namespace ECSave.Format
{
    /// <summary>
    /// 敵対的入力の構造検証（内部設計 6.9、IP-22、ADR-23）。
    /// FormatReader の各 Read で行う検証に加え、セクション重複・単調性を確認する。
    /// </summary>
    public static unsafe class StructureValidator
    {
        /// <summary>セクション範囲の重なりを検出する（6.9）。</summary>
        public static void ValidateNoOverlap(SectionTable table, ulong totalLength)
        {
            int n = table.Count;
            if (n <= 1) return;

            // 素朴な O(n^2) 検出（セクション数は高々4096、通常は数十）
            for (int i = 0; i < n; i++)
            {
                ref var a = ref table.Entries[i];
                ulong aEnd = a.Offset + a.Length;
                if (aEnd < a.Offset || aEnd > totalLength)
                    throw new FormatException(FormatError.CorruptedData, $"section {i} range out of file");
                for (int j = i + 1; j < n; j++)
                {
                    ref var b = ref table.Entries[j];
                    ulong bEnd = b.Offset + b.Length;
                    if (bEnd < b.Offset || bEnd > totalLength)
                        throw new FormatException(FormatError.CorruptedData, $"section {j} range out of file");
                    // 重なり判定： [a.Offset, aEnd) と [b.Offset, bEnd)
                    if (a.Offset < bEnd && b.Offset < aEnd)
                        throw new FormatException(FormatError.CorruptedData, $"sections {i} and {j} overlap");
                }
            }
        }

        /// <summary>共通セクション本体の Count/Stride を検証（6.9）。</summary>
        public static void ValidateCommonSectionBody(ByteReader r, uint sectionLength)
        {
            uint count = r.ReadU32();
            _ = r.ReadU32(); // Flags
            uint stride = r.ReadU32();

            // Count × Stride がセクション長を超えない（checked）
            ulong need = (ulong)count * stride;
            if (need > sectionLength)
                throw new FormatException(FormatError.CorruptedData, $"Count×Stride {need} > section length {sectionLength}");
        }
    }
}
