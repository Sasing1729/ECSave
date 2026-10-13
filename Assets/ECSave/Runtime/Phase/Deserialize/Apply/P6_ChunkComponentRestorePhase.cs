using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class P6_ChunkComponentRestorePhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            EntityManager em)
        {
            for (int s = 0; s < ctx.SectionTable.Count; s++)
            {
                var m = ctx.ModuleBySection[s];
                if (m == null) continue;
                if (m.SectionKind != SectionKind.ChunkComponent) continue;

                var bodyView = GetSectionBody(ref ctx, s);

                bool merged = false;
                var err = RestoreSection(
                    bodyView, ctx.PersistentToEntity, em,
                    (SaveTypeModuleChunkMarker)m, ref merged);

                if (err != SaveLoadErrorCode.None) return err;

                if (merged)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.ChunkComponentMerged,
                        (uint)s,
                        -1,
                        Entity.Null));
                }
            }

            return SaveLoadErrorCode.None;
        }

        private static ByteView GetSectionBody(ref PrephaseContext ctx, int sectionIndex)
        {
            ref var entry = ref ctx.SectionTable.Entries[sectionIndex];
            var sectionView = new ByteView(
                ctx.View.Pointer + (int)entry.Offset, (int)entry.Length);
            int tdSize = (int)(*(uint*)sectionView.Pointer);
            return sectionView.Slice(tdSize, (int)entry.Length - tdSize);
        }

        private static SaveLoadErrorCode RestoreSection(
            ByteView bodyView,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            SaveTypeModuleChunkMarker marker,
            ref bool merged)
        {
            var v = ChunkComponentSectionReader.Read(bodyView);
            int groupCount = (int)v.Header.GroupCount;
            int stride = (int)v.Header.Stride;

            return marker.RestoreChunks(
                v, groupCount, stride, persistentToEntity, em, ref merged);
        }
    }

    internal interface SaveTypeModuleChunkMarker
    {
        SaveLoadErrorCode RestoreChunks(
            in ChunkComponentSectionView view,
            int groupCount,
            int stride,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            ref bool merged);
    }
}
