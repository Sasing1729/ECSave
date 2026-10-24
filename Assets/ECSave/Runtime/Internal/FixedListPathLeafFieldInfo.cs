namespace ECSave.Internal
{
    public struct FixedListTraversalStep
    {
        public int ListOffset;
        public int ElementDataOffset;
        public int Capacity;
        public int ElementStride;
    }

    /// <summary>
    /// 任意深さ（最大 4 段）の FixedList ネストの先にある FixedString のメタデータ。
    /// </summary>
    public struct FixedListPathLeafFieldInfo
    {
        public int FirstContainerOffset;
        public FixedListTraversalStep Step0;
        public FixedListTraversalStep Step1;
        public FixedListTraversalStep Step2;
        public FixedListTraversalStep Step3;
        public int StepCount;
        public int LeafOffset;
        public int LeafCapacity;
        public string Name;
    }
}
