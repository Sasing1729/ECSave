namespace ECSave.Format
{
    /// <summary>
    /// SchemaHash の pointer 版（Format 専用）。
    /// このファイルは Generators.Core には取り込まない。
    /// 取り込むと Generators.Core に allowUnsafeCode: true が必要になるため。
    /// </summary>
    public static unsafe partial class SchemaHashCore
    {
        /// <summary>ゼロコピーでハッシュする。ByteView 経由の読み取りで使う。</summary>
        public static ulong Hash(byte* data, int length, ulong seed = FnvOffsetBasis)
        {
            if (data == null || length <= 0) return seed;
            ulong h = seed;
            for (int i = 0; i < length; i++)
            {
                h ^= data[i];
                h *= FnvPrime;
            }
            return h;
        }
    }
}
