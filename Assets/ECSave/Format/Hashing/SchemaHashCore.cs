namespace ECSave.Format
{
    /// <summary>
    /// SchemaHash のアルゴリズム（内部設計 5.4）。FNV-1a 64bit。
    /// このファイルは Generators.Core と Format の両方にリンクで取り込まれる（3.2節）。
    /// 
    /// 【共有規約】このファイルには unsafe を含めない。
    /// pointer 版は SchemaHashCore.Pointer.cs（Format 専用）に置く。
    /// </summary>
    public static partial class SchemaHashCore
    {
        public const ulong FnvOffsetBasis = 0xcbf29ce484222325UL;
        public const ulong FnvPrime = 0x100000001b3UL;

        public static ulong Hash(byte[] data, ulong seed = FnvOffsetBasis)
        {
            if (data == null) return seed;
            ulong h = seed;
            for (int i = 0; i < data.Length; i++)
            {
                h ^= data[i];
                h *= FnvPrime;
            }
            return h;
        }

        /// <summary>
        /// UTF-8 バイト列としてハッシュ化する。
        ///
        /// <para>
        /// <b>サロゲートペア（BMP 外）対応</b>: Source Generator 側の
        /// <c>ECSave.SourceGenerator.Core.SchemaHashCore.HashUtf8</c> と完全一致させること。
        /// 非 BMP 文字を含む型名でも両者で同じハッシュが得られ、Migration の
        /// <c>NewTypeNameHash</c> 照合が正しく行われる。
        /// </para>
        /// <para>
        /// <b>孤立サロゲート</b>: <c>System.Text.Encoding.UTF8</c> と同様に
        /// U+FFFD (<c>EF BF BD</c>) で置換する。
        /// </para>
        /// </summary>
        public static ulong HashUtf8(string s, ulong seed = FnvOffsetBasis)
        {
            if (string.IsNullOrEmpty(s)) return seed;
            ulong h = seed;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                // サロゲートペア（BMP 外）は 4 バイト UTF-8 としてエンコード
                if (char.IsHighSurrogate(c)
                    && i + 1 < s.Length
                    && char.IsLowSurrogate(s[i + 1]))
                {
                    int cp = char.ConvertToUtf32(c, s[i + 1]);
                    h ^= (byte)(0xF0 | (cp >> 18)); h *= FnvPrime;
                    h ^= (byte)(0x80 | ((cp >> 12) & 0x3F)); h *= FnvPrime;
                    h ^= (byte)(0x80 | ((cp >> 6) & 0x3F)); h *= FnvPrime;
                    h ^= (byte)(0x80 | (cp & 0x3F)); h *= FnvPrime;
                    i++;
                    continue;
                }

                // 孤立サロゲート（ペアにならない high / low）は、
                // System.Text.Encoding.UTF8 と同様に U+FFFD (EF BF BD) で置換する。
                if (char.IsSurrogate(c))
                {
                    h ^= 0xEF; h *= FnvPrime;
                    h ^= 0xBF; h *= FnvPrime;
                    h ^= 0xBD; h *= FnvPrime;
                    continue;
                }

                if (c < 0x80)
                {
                    h ^= (byte)c; h *= FnvPrime;
                }
                else if (c < 0x800)
                {
                    h ^= (byte)(0xC0 | (c >> 6)); h *= FnvPrime;
                    h ^= (byte)(0x80 | (c & 0x3F)); h *= FnvPrime;
                }
                else
                {
                    h ^= (byte)(0xE0 | (c >> 12)); h *= FnvPrime;
                    h ^= (byte)(0x80 | ((c >> 6) & 0x3F)); h *= FnvPrime;
                    h ^= (byte)(0x80 | (c & 0x3F)); h *= FnvPrime;
                }
            }
            return h;
        }
    }
}
