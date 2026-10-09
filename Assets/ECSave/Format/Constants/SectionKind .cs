// Runtime/Format/Constants/SectionKind.cs
namespace ECSave.Format
{
    /// <summary>セクションの種別（内部設計 6.3）。</summary>
    public enum SectionKind : byte
    {
        Unknown = 0,
        Identity = 1,
        Component = 2,
        Buffer = 3,
        Shared = 4,
        ChunkComponent = 5,
    }
}
