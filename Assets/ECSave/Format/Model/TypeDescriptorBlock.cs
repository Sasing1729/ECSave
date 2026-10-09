// Runtime/Format/Model/TypeDescriptorBlock.cs
namespace ECSave.Format
{
    /// <summary>
    /// 型記述子ブロック（内部設計 6.10）。SchemaHash 入力列＋名前列。
    /// </summary>
    public unsafe struct TypeDescriptorBlock
    {
        public ushort EntryCount;
        public ushort RawEntryCount;
        public byte* SchemaHashInput;   // 正規エンコード列
        public int SchemaHashInputLength;
        public byte* NameHeap;          // フィールド名（長さ付き連結）
        public int NameHeapLength;
    }
}
