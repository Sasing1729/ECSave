namespace ECSave.Internal
{
    /// <summary>
    /// FixedString フィールドのメタデータ。Source Generator が生成する。
    /// P7.2 フェーズで「容量ぴったり到達」を検出し、必要なら文字境界で修復する。
    /// </summary>
    public struct FixedStringFieldInfo
    {
        /// <summary>struct 先頭からのバイトオフセット（ushort Length の位置）。</summary>
        public int Offset;

        /// <summary>その FixedString の UTF-8 最大容量（例: FixedString32Bytes なら 29）。</summary>
        public int Capacity;

        /// <summary>診断メッセージ用のフィールド名。</summary>
        public string Name;
    }

    /// <summary>
    /// FixedList&lt;FixedString&gt; フィールドのメタデータ（Runtime 側）。
    /// </summary>
    public struct FixedListFixedStringFieldInfo
    {
        public int ListOffset;
        public int ElementDataOffset;
        public int Capacity;
        public int ElementStride;
        public int ElementCapacity;
        public string Name;
    }
}
