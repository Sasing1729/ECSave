using System;
using System.Collections.Generic;

namespace ECSave.Internal.Contract
{
    public static class GeneratedScopeRegistry
    {
        private static readonly Dictionary<string, ISaveModuleBinder> s_binders = new();

        public static void Register(string scopeId, ISaveModuleBinder binder)
        {
            if (string.IsNullOrEmpty(scopeId))
                throw new ArgumentException(nameof(scopeId));
            if (binder == null)
                throw new ArgumentNullException(nameof(binder));

            if (s_binders.TryGetValue(scopeId, out var existing))
            {
                if (existing.GetType() == binder.GetType())
                    return;

                throw new SaveRegistrationException("ECSAVE-R016",
                    $"Different binder type already registered for scope '{scopeId}'. " +
                    $"Existing: {existing.GetType().FullName}, " +
                    $"New: {binder.GetType().FullName}");
            }

            s_binders[scopeId] = binder;
        }

        public static bool TryGet(string scopeId, out ISaveModuleBinder binder)
            => s_binders.TryGetValue(scopeId, out binder);

        public static void Clear() => s_binders.Clear();

        public static void Remove(string scopeId)
        {
            if (string.IsNullOrEmpty(scopeId)) return;
            s_binders.Remove(scopeId);
        }

        public static IReadOnlyDictionary<string, ISaveModuleBinder> Snapshot()
            => new Dictionary<string, ISaveModuleBinder>(s_binders);

        public static void Restore(IReadOnlyDictionary<string, ISaveModuleBinder> snapshot)
        {
            s_binders.Clear();
            if (snapshot == null) return;

            foreach (var kv in snapshot)
                s_binders[kv.Key] = kv.Value;
        }
    }
}
