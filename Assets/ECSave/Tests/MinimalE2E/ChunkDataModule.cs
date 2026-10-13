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
