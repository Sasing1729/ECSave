using Unity.Entities;

namespace Game.Domain
{
    /// <summary>Phase 5c-3 の Enableable Buffer テスト用。</summary>
    public struct StatusEffect : IBufferElementData, IEnableableComponent
    {
        public int EffectId;
        public int Duration;
    }
}
