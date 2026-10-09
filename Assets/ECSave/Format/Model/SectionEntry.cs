// Runtime/Format/Model/SectionEntry.cs
namespace ECSave.Format
{
    /// <summary>セクション表のエントリ（内部設計 6.3、固定長 48 バイト）。</summary>
    public struct SectionEntry
    {
        public uint SectionId;
        public SectionKind Kind;
        public byte Flags;         // bit0: 型記述子ブロックあり
        public ushort NameLength;
        public ulong NameHash;
        public ulong SchemaHash;
        public ulong Offset;
        public ulong Length;
        public uint NameOffset;
        public uint Reserved;

        public bool HasTypeDescriptor => (Flags & ECSaveFormatConstants.SectionEntryFlags_HasTypeDescriptor) != 0;
    }
}
