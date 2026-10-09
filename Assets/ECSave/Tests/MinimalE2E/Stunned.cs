using Unity.Entities;

namespace Game.Domain
{
    /// <summary>Phase 5b の Enableable テスト用。</summary>
    public struct Stunned : IComponentData, IEnableableComponent
    {
        public int FramesRemaining;
    }
}
