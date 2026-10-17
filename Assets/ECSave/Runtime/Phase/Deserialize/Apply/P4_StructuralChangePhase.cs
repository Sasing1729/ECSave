using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class P4_StructuralChangePhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            DeletionPolicy policy,
            EntityManager em)
        {
            if (policy == DeletionPolicy.None)
                return SaveLoadErrorCode.None;

            int n = ctx.Definition.Modules.Count;

            for (int i = 0; i < n; i++)
            {
                var m = ctx.Definition.Modules[i];
                if (m.SectionKind == SectionKind.ChunkComponent) continue;

                var coveredPids = CollectCoveredPids(ref ctx, i);

                try
                {
                    for (int pid = 0; pid < ctx.PersistentToEntity.Length; pid++)
                    {
                        if (coveredPids.Contains((uint)pid)) continue;

                        var e = ctx.PersistentToEntity[pid];
                        if (e == Entity.Null) continue;
                        if (!em.Exists(e)) continue;

                        m.RemoveFromEntity(e, em);
                    }
                }
                finally
                {
                    if (coveredPids.IsCreated) coveredPids.Dispose();
                }
            }

            return SaveLoadErrorCode.None;
        }

        private static NativeParallelHashSet<uint> CollectCoveredPids(
            ref PrephaseContext ctx, int moduleIndex)
        {
            var set = new NativeParallelHashSet<uint>(64, Allocator.TempJob);

            for (int s = 0; s < ctx.SectionTable.Count; s++)
            {
                var m = ctx.ModuleBySection[s];
                if (m == null) continue;
                if (m.RegistrationId != moduleIndex) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, s);

                var v = CommonSectionReader.Read(bodyView);
                int count = (int)v.Header.Count;
                for (int k = 0; k < count; k++)
                    set.Add(v.EntityIndex[k]);
            }

            return set;
        }
    }
}
