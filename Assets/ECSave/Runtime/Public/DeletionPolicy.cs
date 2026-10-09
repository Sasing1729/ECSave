// Runtime/Public/DeletionPolicy.cs
namespace ECSave
{
    /// <summary>復元領域内での削除の挙動（外部設計 12.2.4）。</summary>
    public enum DeletionPolicy
    {
        None = 0,
        ByRegistration = 1,
        ByScope = 2,
    }
}
