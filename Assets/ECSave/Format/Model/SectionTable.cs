// Runtime/Format/Model/SectionTable.cs
namespace ECSave.Format
{
    /// <summary>セクション表（内部設計 6.3）。エントリ列＋名前ヒープを保持する。</summary>
    public unsafe struct SectionTable
    {
        public SectionEntry* Entries;
        public int Count;
        public byte* NameHeap;   // UTF-8 連結
        public int NameHeapLength;
    }
}
