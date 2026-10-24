namespace ECSave.Format
{
    /// <summary>
    /// raw レイアウトと dataStride レイアウトの相互変換を担う共通ヘルパ。
    ///
    /// <para>
    /// <b>raw レイアウト</b>: メモリ上の型 <c>T</c> の生バイト列。Entity フィールドは 8B。
    /// </para>
    /// <para>
    /// <b>dataStride レイアウト</b>: Format 層のレコード列。Entity フィールドは 4B の pid に置換される。
    /// </para>
    /// <para>
    /// <c>EntityRefOffsets</c> が空の場合、両レイアウトはバイト単位で一致する。
    /// したがって本ヘルパの変換関数は恒等写像となる。
    /// </para>
    /// <para>
    /// <b>変換の性質</b>:
    /// <see cref="RawToDataOffset"/> は多対一（8B → 4B 圧縮により raw 上で連続する
    /// 複数の位置が同じ data 位置に写像される）。そのため往復恒等性は
    /// <c>data → raw → data</c> の方向でのみ成立する。
    /// </para>
    /// </summary>
    public static class SectionLayoutHelper
    {
        /// <summary>
        /// raw レイアウト上のオフセットを dataStride レイアウト上のオフセットに変換する。
        ///
        /// <para>
        /// Entity は raw 8B → data 4B なので、<paramref name="rawOffset"/> より前に
        /// 存在する Entity フィールド数 × 4 を減算する。
        /// </para>
        /// <para>
        /// 前提: <paramref name="refs"/> は昇順。
        /// </para>
        /// <para>
        /// <paramref name="rawOffset"/> が Entity フィールド自身の先頭（= いずれかの
        /// <c>refs[i]</c>）を指す場合、その ref は「前方」にカウントされないため、
        /// 戻り値は Entity のデータ内オフセット（pid 位置）になる。
        /// </para>
        /// </summary>
        public static int RawToDataOffset(int rawOffset, int[] refs)
        {
            if (refs == null || refs.Length == 0) return rawOffset;

            int shift = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                if (refs[i] >= rawOffset) break;  // refs は昇順
                shift += 4;
            }
            return rawOffset - shift;
        }

        /// <summary>
        /// dataStride レイアウト上のオフセットを raw レイアウト上のオフセットに変換する。
        ///
        /// <para>
        /// <see cref="RawToDataOffset"/> の逆変換（ただし多対一のため、「最小の raw 前像」を返す）。
        /// これにより <c>RawToDataOffset(DataToRawOffset(data)) == data</c> が全ての data で成立する。
        /// </para>
        /// <para>
        /// 逆に <c>DataToRawOffset(RawToDataOffset(raw)) == raw</c> は、raw が Entity 内部や
        /// Entity 直後の非境界位置を指す場合には成立しない（設計上の性質）。
        /// </para>
        /// </summary>
        public static int DataToRawOffset(int dataOffset, int[] refs)
        {
            if (refs == null || refs.Length == 0) return dataOffset;

            // refs[i] が data レイアウト上でどこに位置するかを順に調べ、
            // dataOffset がその位置より後ろなら shift を加算する。
            //
            // 例: refs=[4], data=1:
            //   compressedPos = 4 - 0 = 4. 4 >= 1 → break. return 1 + 0 = 1.
            // 例: refs=[4], data=5:
            //   compressedPos = 4 - 0 = 4. 4 >= 5 → false. shift=4.
            //   return 5 + 4 = 9.
            int shift = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                int compressedPos = refs[i] - shift;
                if (compressedPos >= dataOffset) break;
                shift += 4;
            }
            return dataOffset + shift;
        }
    }
}
