using ECSave.Format;

namespace ECSave.EditorTools
{
    internal sealed class SectionInfo
    {
        public int Index;
        public uint SectionId;
        public SectionKind Kind;
        public byte Flags;
        public ushort NameLength;
        public ulong NameHash;
        public ulong SchemaHash;
        public ulong Offset;
        public ulong Length;
        public uint NameOffset;
        public string Name;
        public bool HasTypeDescriptor;

        public bool TypeDescriptorAvailable;
        public ushort EntryCount;
        public ushort RawEntryCount;
        public byte[] SchemaHashInput;
        public string[] FieldNames;

        public bool IsChunkComponent;
        public uint GroupCount;
        public uint Stride;
        public uint[] MemberOffset;
        public uint[] MemberEntityIndex;
    }
}
