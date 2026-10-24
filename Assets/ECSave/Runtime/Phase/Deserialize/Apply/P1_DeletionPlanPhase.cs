using ECSave.Format;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class P1_DeletionPlanPhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            RestorationScope scope,
            DeletionPolicy policy,
            NativeList<Entity> destroyTargets)
        {
            if (policy == DeletionPolicy.None)
                return SaveLoadErrorCode.None;

            var em = ctx.World.EntityManager;
            int cap = ctx.PersistentToEntity.Length > 0 ? ctx.PersistentToEntity.Length : 1;

            var updateTargets = new NativeParallelHashSet<Entity>(cap, Allocator.TempJob);
            NativeParallelHashSet<Entity> registeredSet = default;

            try
            {
                for (int i = 0; i < ctx.PersistentToEntity.Length; i++)
                {
                    var e = ctx.PersistentToEntity[i];
                    if (e != Entity.Null) updateTargets.Add(e);
                }

                if (policy == DeletionPolicy.ByRegistration)
                {
                    registeredSet = new NativeParallelHashSet<Entity>(256, Allocator.TempJob);
                    for (int i = 0; i < ctx.Definition.Modules.Count; i++)
                    {
                        var m = ctx.Definition.Modules[i];
                        var q = m.BuildQuery(em);
                        using var es = q.ToEntityArray(Allocator.Temp);

                        for (int j = 0; j < es.Length; j++)
                            registeredSet.Add(es[j]);
                    }
                }

                if (scope.IsEntireWorld)
                {
                    using var all = em.GetAllEntities(Allocator.TempJob);
                    for (int i = 0; i < all.Length; i++)
                    {
                        var e = all[i];
                        if (em.HasComponent<Prefab>(e)) continue;

                        // Review #5b: Tombstone Registry singleton は削除対象から除外する。
                        // P6.5 で復元した Registry が P7 で破棄されると Tombstone が消える。
                        if (em.HasComponent<SubSceneTombstoneRegistryTag>(e)) continue;

                        if (updateTargets.Contains(e)) continue;
                        if (policy == DeletionPolicy.ByRegistration
                            && !registeredSet.Contains(e)) continue;
                        destroyTargets.Add(e);
                    }
                }
                else
                {
                    var regionQuery = scope.Query;
                    using var regionEntities = regionQuery.ToEntityArray(Allocator.TempJob);
                    for (int i = 0; i < regionEntities.Length; i++)
                    {
                        var e = regionEntities[i];
                        if (em.HasComponent<Prefab>(e)) continue;
                        if (em.HasComponent<SubSceneTombstoneRegistryTag>(e)) continue;

                        if (updateTargets.Contains(e)) continue;
                        if (policy == DeletionPolicy.ByRegistration
                            && !registeredSet.Contains(e)) continue;
                        destroyTargets.Add(e);
                    }
                }

                return SaveLoadErrorCode.None;
            }
            finally
            {
                if (registeredSet.IsCreated) registeredSet.Dispose();
                if (updateTargets.IsCreated) updateTargets.Dispose();
            }
        }
    }
}
