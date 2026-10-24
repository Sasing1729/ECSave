using System;
using System.Collections.Generic;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Entities;

namespace ECSave.Phase.Serialize
{
    internal static class S2_6_TombstoneCapturePhase
    {
        public sealed class Result
        {
            public bool HasSection { get; set; }
            public IReadOnlyList<CapturedHandler> Captured { get; set; }
                = Array.Empty<CapturedHandler>();
        }

        public sealed class CapturedHandler
        {
            public SubSceneDestroyedIdentityHandler Handler { get; }
            public IReadOnlyList<byte[]> Keys { get; }
            public int SectionBodySize { get; }
            public string SectionName { get; }

            public CapturedHandler(
                SubSceneDestroyedIdentityHandler handler,
                IReadOnlyList<byte[]> keys,
                string sectionName)
            {
                Handler = handler;
                Keys = keys;
                SectionName = sectionName;
                SectionBodySize = SubSceneTombstoneLayout.ComputeBodySize(keys.Count, handler.KeyStride);
            }
        }

        public static Result Execute(
            SubSceneDefinition def,
            World world,
            IReadOnlyList<IdentityModule> identities = null)
        {
            var result = new Result();
            if (def == null || !def.HasTombstones) return result;

            // Review #5c: def.DestroyedHandlers は SaveScope.GetSaveSystem() 時点の
            // スナップショット。MarkDestroyed<T> が GetSaveSystem() より後に呼ばれると
            // 空のままになり、Tombstone が一切保存されない。
            // def 側が空ならグローバルレジストリから取得し、登録済み Identity でフィルタする。
            IReadOnlyList<SubSceneDestroyedIdentityHandler> handlers = def.DestroyedHandlers;
            if (handlers == null || handlers.Count == 0)
            {
                handlers = SubSceneTombstone.GetRegisteredHandlers();

                if (identities != null && identities.Count > 0)
                {
                    var registered = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < identities.Count; i++)
                    {
                        var im = identities[i];
                        if (im != null && !string.IsNullOrEmpty(im.FullTypeName))
                            registered.Add(im.FullTypeName);
                    }

                    var filtered = new List<SubSceneDestroyedIdentityHandler>();
                    for (int i = 0; i < handlers.Count; i++)
                    {
                        var h = handlers[i];
                        if (h != null && registered.Contains(h.FullTypeName))
                            filtered.Add(h);
                    }
                    handlers = filtered;
                }
            }

            if (handlers == null || handlers.Count == 0) return result;

            var captured = new List<CapturedHandler>();

            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (h == null) continue;

                IReadOnlyList<byte[]> keys;
                try
                {
                    keys = h.Snapshot(world) ?? Array.Empty<byte[]>();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[ECSave] Tombstone snapshot threw for {h.FullTypeName}: {ex}");
                    keys = Array.Empty<byte[]>();
                }

                if (keys.Count == 0) continue;

                captured.Add(new CapturedHandler(
                    h, keys, SubSceneTombstoneSection.SectionNameFor(h.FullTypeName)));
            }

            result.HasSection = captured.Count > 0;
            result.Captured = captured;
            return result;
        }
    }
}
