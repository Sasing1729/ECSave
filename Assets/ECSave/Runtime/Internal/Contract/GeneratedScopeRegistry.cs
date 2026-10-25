using System.Collections.Generic;

namespace ECSave.Internal.Contract
{
    public static class GeneratedScopeRegistry
    {
        private static readonly Dictionary<string, ISaveModuleBinder> s_binders
            = new Dictionary<string, ISaveModuleBinder>();
        private static readonly Dictionary<string, ISubSceneBinder> s_subSceneBinders
            = new Dictionary<string, ISubSceneBinder>();

        public static void Register(string scopeId, ISaveModuleBinder binder)
        {
            if (binder == null) return;

            if (s_binders.TryGetValue(scopeId, out var existing)
                && existing.GetType() != binder.GetType())
            {
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.ScopeBinderConflict,
                    $"Scope '{scopeId}' already registered with a different binder type");
            }

            s_binders[scopeId] = binder;

            if (binder is ISubSceneBinder sub)
                s_subSceneBinders[scopeId] = sub;
        }

        public static bool TryGet(string scopeId, out ISaveModuleBinder binder)
            => s_binders.TryGetValue(scopeId, out binder);

        public static bool TryGetSubScene(string scopeId, out ISubSceneBinder binder)
            => s_subSceneBinders.TryGetValue(scopeId, out binder);

        internal static void Clear()
        {
            s_binders.Clear();
            s_subSceneBinders.Clear();
        }

        internal static void Remove(string scopeId)
        {
            s_binders.Remove(scopeId);
            s_subSceneBinders.Remove(scopeId);
        }

        internal static IReadOnlyDictionary<string, ISaveModuleBinder> Snapshot()
            => new Dictionary<string, ISaveModuleBinder>(s_binders);

        internal static void Restore(IReadOnlyDictionary<string, ISaveModuleBinder> snapshot)
        {
            s_binders.Clear();
            s_subSceneBinders.Clear();
            if (snapshot == null) return;

            foreach (var kv in snapshot)
            {
                s_binders[kv.Key] = kv.Value;
                if (kv.Value is ISubSceneBinder sub)
                    s_subSceneBinders[kv.Key] = sub;
            }
        }

        /// <summary>
        /// Domain Reload 無効時の静的状態リセット。
        ///
        /// <para>
        /// <b>ロード順序の保証</b>: Source Generator が生成する <c>Register()</c> は
        /// <see cref="UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded"/> で
        /// 実行される。本リセットを <c>SubsystemRegistration</c> に置くことで、
        /// 「リセット → Register」という順序を保証する。
        /// </para>
        /// <para>
        /// <b>Unity の制約</b>: 同一 load type 内の複数 <c>RuntimeInitializeOnLoadMethod</c>
        /// の実行順序は Unity が保証しない。したがって Reset と Register は
        /// 必ず異なる load type に分ける必要がある
        /// （<see cref="UnityEngine.RuntimeInitializeLoadType"/> の値順で実行される）。
        /// </para>
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            s_binders.Clear();
            s_subSceneBinders.Clear();
        }
    }
}
