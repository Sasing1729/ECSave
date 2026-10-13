namespace ECSave.EditorTools
{
    internal sealed class SaveDataInspection
    {
        public string FilePath;
        public byte[] Data;
        public int FileSize;

        public bool Loaded;
        public string ErrorMessage;

        public uint Magic;
        public ushort FormatVersion;
        public ushort HeaderFlags;
        public uint HeaderSize;
        public int GameDataVersion;
        public int LogicalEntityCount;
        public int SectionCount;
        public ulong SectionTableOffset;
        public ulong TotalLength;
        public uint NameHeapLength;
        public ushort WorldIdLength;
        public string WorldId;

        public SectionInfo[] Sections;
    }
}
