using ECSave.Format;
using ECSave.Internal;
using Game.Domain;

namespace Game.MinimalE2E
{
    internal sealed class ChunkDataModule : SaveTypeModuleChunk<ChunkData>
    {
        public const string TypeFullName = "Game.Domain.ChunkData";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = { "Value" };

        // B-5: SaveTypeModuleChunk<T>.EntityRefOffsets が abstract 化されたため、
        // サブクラスは必ず override を提供する必要がある。
        // 本モジュールは Entity フィールドを持たないため空配列。
        private static readonly int[] s_entityRefOffsets = System.Array.Empty<int>();

        public override int[] EntityRefOffsets => s_entityRefOffsets;

        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        private unsafe static byte[] BuildInput()
        {
            var buf = new byte[2 + 16];
            buf[0] = (byte)(sizeof(ChunkData) & 0xFF);
            buf[1] = (byte)(sizeof(ChunkData) >> 8);
            buf[2] = (byte)SchemaHashKind.Int32;
            buf[3] = 4;
            buf[4] = 0;
            return buf;
        }
    }
}
