// Runtime/Format/Constants/EntityRefConstants.cs
namespace ECSave.Format
{
    /// <summary>Entity 参照の符号化（内部設計 6.5）。</summary>
    public static class EntityRefConstants
    {
        /// <summary>元の値が Entity.Null。Warning なし。</summary>
        public const uint NullRef = 0xFFFFFFFFu;

        /// <summary>保存時に解決できなかった参照。Entity.Null に置換、Warning 記録。</summary>
        public const uint UnresolvedRef = 0xFFFFFFFEu;

        /// <summary>永続 ID の有効範囲 [0, LogicalEntityCount)。</summary>
        public static bool IsValidPersistentId(uint value, int logicalEntityCount)
            => value < (uint)logicalEntityCount;

        /// <summary>予約値かどうか。</summary>
        public static bool IsReserved(uint value)
            => value == NullRef || value == UnresolvedRef;
    }
}
