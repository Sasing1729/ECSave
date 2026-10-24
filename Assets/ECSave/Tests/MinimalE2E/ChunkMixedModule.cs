using ECSave.Format;
using ECSave.Internal;
using Game.Domain;

namespace Game.MinimalE2E
{
    internal sealed class ChunkMixedModule : SaveTypeModuleChunk<ChunkMixed>
    {
        public const string TypeFullName = "Game.Domain.ChunkMixed";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = { "Name", "Target", "Value" };
        private static readonly int[] s_entityRefOffsets = { 32 };

        public override int[] EntityRefOffsets => s_entityRefOffsets;
        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        private static readonly FixedStringFieldInfo[] s_fixedStringFields = new FixedStringFieldInfo[]
        {
            new FixedStringFieldInfo
            {
                Offset = 0,
                Capacity = 29,   // sizeof(FixedString32Bytes) - 3
                Name = "Name",
            },
        };

        public override FixedStringFieldInfo[] FixedStringFields => s_fixedStringFields;

        private unsafe static byte[] BuildInput()
        {
            var buf = new byte[2 + 16 + 16 + 16];
            buf[0] = (byte)(sizeof(ChunkMixed) & 0xFF);
            buf[1] = (byte)(sizeof(ChunkMixed) >> 8);
            WriteEntry(buf, 2, SchemaHashKind.FixedString, 32, 0);
            WriteEntry(buf, 18, SchemaHashKind.Entity, 8, 32);
            WriteEntry(buf, 34, SchemaHashKind.Int32, 4, 40);
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
