// Runtime/Public/LoadWarningKind.cs
namespace ECSave
{
    /// <summary>LoadWarning の種別（外部設計 13.4）。</summary>
    public enum LoadWarningKind : byte
    {
        EntityReferenceUnresolved = 0,
        UnknownSectionIgnored = 1,
        ChunkComponentMerged = 2,
    }
}
