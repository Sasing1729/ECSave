using System;
using System.Collections.Generic;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Phase.Serialize
{
    internal static class S2_7_PrefabIdentityCapturePhase
    {
        public sealed class Result
        {
            public bool HasSection { get; set; }
            public int SectionBodySize { get; set; }
            public IReadOnlyList<PrefabIdentityRecord> Records { get; set; }
                = Array.Empty<PrefabIdentityRecord>();
        }

        public static Result Execute(
            SubSceneDefinition def,
            NativeList<Entity> entities,
            NativeParallelHashMap<Entity, int> idOf)
        {
            var result = new Result();
            if (def == null || !def.HasPrefabIdentity) return result;
            if (entities.Length == 0) return result;

            var records = new List<PrefabIdentityRecord>();
            var provider = def.PrefabIdentityProvider;

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (e == Entity.Null) continue;

                PrefabIdentityInfo? info;
                try
                {
                    info = provider(e);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[ECSave] PrefabIdentityProvider threw for entity {e}: {ex}");
                    continue;
                }

                if (!info.HasValue) continue;
                if (info.Value.PrefabGuid == 0UL) continue;
                if (!idOf.TryGetValue(e, out int pid)) continue;

                records.Add(new PrefabIdentityRecord(
                    (uint)pid, info.Value.PrefabGuid, info.Value.SchemaHash));
            }

            if (records.Count == 0) return result;

            result.HasSection = true;
            result.Records = records;
            result.SectionBodySize = PrefabIdentityLayout.ComputeBodySize(records.Count);
            return result;
        }
    }
}
