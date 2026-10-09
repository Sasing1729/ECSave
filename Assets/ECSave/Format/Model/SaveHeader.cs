// Runtime/Format/Model/SaveHeader.cs
namespace ECSave.Format
{
    /// <summary>
    /// ヘッダ（内部設計 6.2）。固定部＋WorldId を保持する。
    /// </summary>
    public unsafe struct SaveHeader
    {
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
        public ushort Reserved;

        /// <summary>WorldId は UTF-8 バイト列。所有権は読み取り側のバッファにある。</summary>
        public unsafe byte* WorldIdPtr;
    }
}
