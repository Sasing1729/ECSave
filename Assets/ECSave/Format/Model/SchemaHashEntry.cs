// Runtime/Format/Model/SchemaHashEntry.cs
namespace ECSave.Format
{
    /// <summary>
    /// SchemaHash 入力列のエントリ（内部設計 5.4）。
    /// 固定長 16 バイト（通常）または 5 バイト（Raw）。
    /// </summary>
    public struct SchemaHashEntry
    {
        public SchemaHashKind Kind;
        public ushort Size;
        public ushort Offset;
        public ushort Reserved;     // Raw では未使用
        public SchemaHashKind ElementKind;
        public ulong ElementHash;
    }

    public struct SchemaHashRawEntry
    {
        public ushort Offset;
        public ushort Length;
    }
}
