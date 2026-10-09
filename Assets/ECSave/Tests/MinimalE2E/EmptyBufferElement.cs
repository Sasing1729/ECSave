using Unity.Entities;

namespace Game.Domain
{
    /// <summary>
    /// Phase 5c-4 のゼロサイズ Buffer + Enableable テスト用。
    /// フィールドを持たない IBufferElementData + IEnableableComponent。
    /// </summary>
    public struct EmptyBufferElement : IBufferElementData, IEnableableComponent
    {
    }
}
