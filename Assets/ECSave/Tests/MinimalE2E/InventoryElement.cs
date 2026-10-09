using Unity.Entities;

namespace Game.Domain
{
    /// <summary>Phase 5c-2 の Buffer テスト用。</summary>
    public struct InventoryElement : IBufferElementData
    {
        public int ItemId;
        public int Count;
    }
}
