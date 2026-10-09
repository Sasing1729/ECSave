using Unity.Entities;

namespace Game.Domain
{
    /// <summary>
    /// Phase 5c-4 のゼロサイズ Enableable テスト用。
    /// フィールドを持たない IComponentData + IEnableableComponent。
    /// </summary>
    public struct TagComponent : IComponentData, IEnableableComponent
    {
    }
}
