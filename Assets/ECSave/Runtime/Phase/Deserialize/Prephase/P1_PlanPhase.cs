using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class P1_PlanPhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            DeletionPolicy policy)
        {
            BuildRegisteredTargets(ref ctx);

            if (ctx.Definition.Identities == null || ctx.Definition.Identities.Count == 0)
                return SaveLoadErrorCode.None;

            int idCount = ctx.Definition.Identities.Count;
            var index = new IdentityIndex(1024, idCount, Allocator.TempJob);

            try
            {
                var em = ctx.World.EntityManager;

                for (int t = 0; t < idCount; t++)
                {
                    var im = ctx.Definition.Identities[t];
                    var q = im.BuildQuery(em);
                    var err = im.BuildWorldIndex(em, q, ref index, (uint)im.RegistrationId);
                    if (err != SaveLoadErrorCode.None) return err;
                }

                for (int s = 0; s < ctx.SectionTable.Count; s++)
                {
                    ref var entry = ref ctx.SectionTable.Entries[s];
                    if (entry.Kind != SectionKind.Identity) continue;

                    var im = FindIdentity(ctx.Definition, entry.NameHash);
                    if (im == null) continue;

                    var bodyView = GetSectionBody(ref ctx, s);
                    var err = im.BuildSaveIndex(bodyView, ref index, (uint)im.RegistrationId);
                    if (err != SaveLoadErrorCode.None) return err;
                }

                return MatchAndFill(ref ctx, ref index);
            }
            finally
            {
                index.Dispose();
            }
        }

        private static void BuildRegisteredTargets(ref PrephaseContext ctx)
        {
            int n = ctx.Definition.Modules.Count;
            ctx.RegisteredTargets = new NativeParallelHashSet<Entity>[n];
            var em = ctx.World.EntityManager;

            for (int i = 0; i < n; i++)
            {
                var m = ctx.Definition.Modules[i];
                var set = new NativeParallelHashSet<Entity>(256, Allocator.TempJob);
                var q = m.BuildQuery(em);
                using var entities = q.ToEntityArray(Allocator.Temp);
                for (int k = 0; k < entities.Length; k++)
                    set.Add(entities[k]);
                ctx.RegisteredTargets[i] = set;
            }
        }

        private static ByteView GetSectionBody(ref PrephaseContext ctx, int sectionIndex)
        {
            ref var entry = ref ctx.SectionTable.Entries[sectionIndex];
            var sectionView = new ByteView(
                ctx.View.Pointer + (int)entry.Offset, (int)entry.Length);
            int tdSize = (int)(*(uint*)sectionView.Pointer);
            return sectionView.Slice(tdSize, (int)entry.Length - tdSize);
        }

        private static SaveLoadErrorCode MatchAndFill(
            ref PrephaseContext ctx,
            ref IdentityIndex index)
        {
            int cap = ctx.PersistentToEntity.Length;
            if (cap < 16) cap = 16;

            var entityToPid = new NativeParallelHashMap<Entity, int>(cap, Allocator.TempJob);

            try
            {
                for (int s = 0; s < ctx.SectionTable.Count; s++)
                {
                    ref var entry = ref ctx.SectionTable.Entries[s];
                    if (entry.Kind != SectionKind.Identity) continue;

                    var im = FindIdentity(ctx.Definition, entry.NameHash);
                    if (im == null) continue;

                    var bodyView = GetSectionBody(ref ctx, s);
                    var v = CommonSectionReader.Read(bodyView);
                    int count = (int)v.Header.Count;
                    uint typeIndex = (uint)im.RegistrationId;

                    for (int i = 0; i < count; i++)
                    {
                        uint pid = v.EntityIndex[i];
                        if (pid >= ctx.PersistentToEntity.Length)
                            return SaveLoadErrorCode.ValidationFailed;

                        byte* key = v.Records + i * im.KeyStride;

                        Entity matched;
                        if (!index.TryMatchWorldEntity(typeIndex, key, im.KeyStride, out matched))
                            continue;

                        if (matched == Entity.Null)
                            continue;

                        Entity current = ctx.PersistentToEntity[(int)pid];
                        if (current != Entity.Null && current != matched)
                            return SaveLoadErrorCode.ValidationFailed;

                        if (entityToPid.TryGetValue(matched, out int existingPid)
                            && existingPid != (int)pid)
                            return SaveLoadErrorCode.ValidationFailed;

                        ctx.PersistentToEntity[(int)pid] = matched;
                        entityToPid[matched] = (int)pid;
                    }
                }

                return SaveLoadErrorCode.None;
            }
            finally
            {
                if (entityToPid.IsCreated) entityToPid.Dispose();
            }
        }

        private static IdentityModule FindIdentity(SaveDefinition def, ulong nameHash)
        {
            if (def.Identities == null) return null;
            for (int i = 0; i < def.Identities.Count; i++)
            {
                var m = def.Identities[i];
                if (SchemaHashCore.HashUtf8(m.FullTypeName) == nameHash)
                    return m;
            }
            return null;
        }
    }
}
