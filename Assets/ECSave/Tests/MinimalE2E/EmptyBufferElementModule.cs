using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;

namespace Game.MinimalE2E
{
    /// <summary>
    /// ゼロサイズ Enableable Buffer の手書きモジュール。
    /// Phase 3 で Generator がこの形のコードを生成する。
    /// </summary>
    internal sealed class EmptyBufferElementModule
        : SaveTypeModuleBufferEnableable<EmptyBufferElement>
    {
        public const string TypeFullName = "Game.Domain.EmptyBufferElement";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = System.Array.Empty<string>();

        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public override SectionKind SectionKind => SectionKind.Buffer;
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        /// <summary>ゼロサイズ要素の SchemaHash 入力列：先頭 u16 のサイズ（0）のみ。</summary>
        private static byte[] BuildInput()
        {
            var buf = new byte[2];
            buf[0] = 0;
            buf[1] = 0;
            return buf;
        }
    }
}
