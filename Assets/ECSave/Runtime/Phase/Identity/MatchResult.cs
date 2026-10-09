// Runtime/Phase/Identity/MatchResult.cs
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// 識別キー照合の結果（9.4節）。
    /// </summary>
    public struct MatchResult
    {
        public int SaveEntryIndex;   // SaveData 側エントリの番号。マッチしなければ -1
        public Entity WorldEntity;
        public bool Matched;
    }

    /// <summary>
    /// 照合矛盾の種類（2.2.2.1節）。
    /// </summary>
    public enum IdentityConflictKind : byte
    {
        None = 0,
        Conflict1,  // 1 つの SaveData Entity が複数の World Entity に一致
        Conflict2,  // 複数の SaveData Entity が同じ World Entity に一致
    }
}
