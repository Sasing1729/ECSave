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

        public static Result Execute(SubSceneDefinition def, World world)
        {
            var result = new Result();
            if (def == null || !def.HasTombstones) return result;

            var captured = new List<CapturedHandler>();
            var handlers = def.DestroyedHandlers;

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
