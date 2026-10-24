using ECSave.Format;
using ECSave.Internal;
using Game.Domain;

namespace Game.MinimalE2E
{
    internal sealed class ChunkWithRefModule : SaveTypeModuleChunk<ChunkWithRef>
    {
        public const string TypeFullName = "Game.Domain.ChunkWithRef";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = { "Target", "Value" };
        private static readonly int[] s_entityRefOffsets = { 0 };

        public override int[] EntityRefOffsets => s_entityRefOffsets;
        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        private unsafe static byte[] BuildInput()
        {
            var buf = new byte[2 + 16 + 16];
            buf[0] = (byte)(sizeof(ChunkWithRef) & 0xFF);
            buf[1] = (byte)(sizeof(ChunkWithRef) >> 8);
            WriteEntry(buf, 2, SchemaHashKind.Entity, 8, 0);
            WriteEntry(buf, 18, SchemaHashKind.Int32, 4, 8);
            return buf;
        }

        private static void WriteEntry(
            byte[] buf, int offset, SchemaHashKind kind, ushort size, ushort fieldOffset)
        {
            buf[offset + 0] = (byte)kind;
            buf[offset + 1] = (byte)(size & 0xFF);
            buf[offset + 2] = (byte)(size >> 8);
            buf[offset + 3] = (byte)(fieldOffset & 0xFF);
            buf[offset + 4] = (byte)(fieldOffset >> 8);
        }
    }
}
