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

        public static ulong HashUtf8(string s, ulong seed = FnvOffsetBasis)
        {
            if (string.IsNullOrEmpty(s)) return seed;
            ulong h = seed;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
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
