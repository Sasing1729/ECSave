// Runtime/Public/RestorationScope.cs
using Unity.Entities;

namespace ECSave
{
    /// <summary>
    /// ロード時に操作対象とする World の既存 Entity の範囲（外部設計 12.2.2）。
    /// SaveData からの新規生成を制限しない（IP-20）。
    /// </summary>
    public readonly struct RestorationScope
    {
        public readonly bool IsEntireWorld;
        public readonly EntityQuery Query;

        private RestorationScope(bool entireWorld, EntityQuery query)
        {
            IsEntireWorld = entireWorld;
            Query = query;
        }

        public static RestorationScope EntireWorld => new RestorationScope(true, default);

        public static RestorationScope Custom(EntityQuery query)
            => new RestorationScope(false, query);
    }
}
