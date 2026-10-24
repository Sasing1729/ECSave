using System;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave.Phase.Deserialize.Apply
{
    internal static class P7_5_PrefabIdentityApplyPhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx, EntityManager em)
        {
            var list = ctx.PrefabMigrationsToApply;
            if (list == null || list.Count == 0) return SaveLoadErrorCode.None;

            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                if (entry.PersistentId >= (uint)ctx.PersistentToEntity.Length) continue;

                var target = ctx.PersistentToEntity[(int)entry.PersistentId];
                if (target == Entity.Null) continue;
                if (!em.Exists(target)) continue;
                if (entry.Steps == null) continue;

                for (int s = 0; s < entry.Steps.Length; s++)
                {
                    var step = entry.Steps[s];
                    if (step == null) continue;

                    var context = new PrefabMigrationContext
                    {
                        EntityManager = em,
                        Entity = target,
                        SourcePrefabGuid = entry.SourcePrefabGuid,
                        TargetPrefabGuid = entry.SourcePrefabGuid,
                        SourceSchemaHash = entry.SourceSchemaHash,
                        TargetSchemaHash = step.NewPrefabSchemaHash,
                    };

                    try
                    {
                        step.Migrate(ref context);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError(
                            $"[ECSave] PrefabMigration threw for entity {target} (step {s}): {ex}");
                        return SaveLoadErrorCode.WorldModificationFailed;
                    }
                }
            }
            return SaveLoadErrorCode.None;
        }
    }
}
