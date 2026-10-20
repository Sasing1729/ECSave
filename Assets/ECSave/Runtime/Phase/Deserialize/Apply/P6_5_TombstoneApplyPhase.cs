using System;
using System.Collections.Generic;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Phase.Deserialize.Apply
{
    internal static class P6_5_TombstoneApplyPhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            EntityManager em,
            NativeList<Entity> destroyTargets)
        {
            var def = ctx.Definition.SubScene;
            if (def == null || !def.HasTombstones) return SaveLoadErrorCode.None;

            var entries = ctx.TombstoneSections;
            if (entries == null || entries.Count == 0) return SaveLoadErrorCode.None;

            Entity singleton;
            try
            {
                singleton = SubSceneTombstoneRegistry.GetOrCreate(em);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ECSave] Failed to create Tombstone registry: {ex}");
                return SaveLoadErrorCode.WorldModificationFailed;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Handler == null) continue;

                var keys = new List<byte[]>((int)e.View.Count);
                for (int k = 0; k < e.View.Count; k++)
                {
                    var bytes = new byte[e.View.KeyStride];
                    unsafe
                    {
                        byte* src = e.View.GetKey(k);
                        for (int b = 0; b < e.View.KeyStride; b++) bytes[b] = src[b];
                    }
                    keys.Add(bytes);
                }

                try
                {
                    e.Handler.RestoreToRegistry(em, singleton, keys);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[ECSave] Tombstone restore threw for {e.Handler.FullTypeName}: {ex}");
                    return SaveLoadErrorCode.WorldModificationFailed;
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Handler == null) continue;

                var local = new NativeList<Entity>(16, Allocator.TempJob);
                try
                {
                    e.Handler.CollectMatches(em, singleton, local);
                    for (int k = 0; k < local.Length; k++)
                        destroyTargets.Add(local[k]);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[ECSave] Tombstone collect threw for {e.Handler.FullTypeName}: {ex}");
                    return SaveLoadErrorCode.WorldModificationFailed;
                }
                finally
                {
                    if (local.IsCreated) local.Dispose();
                }
            }

            return SaveLoadErrorCode.None;
        }
    }
}
