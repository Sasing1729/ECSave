using Unity.Entities;

namespace Game.Domain
{
    /// <summary>Phase 4 の最小 E2E 対象型。</summary>
    public struct Health : IComponentData
    {
        public int Current;
        public int Max;
    }
}
