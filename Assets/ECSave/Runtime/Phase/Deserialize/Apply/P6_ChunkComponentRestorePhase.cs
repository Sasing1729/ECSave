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

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, s);

                bool merged = false;
                var err = RestoreSection(
                    bodyView, ctx.PersistentToEntity, em,
                    (SaveTypeModuleChunkMarker)m, ref merged,
                    out int missingCount, out int firstMissingPid);

                if (err != SaveLoadErrorCode.None) return err;

                if (merged)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.ChunkComponentMerged,
                        (uint)s,
                        -1,
                        Entity.Null));
                }

                // P1: ChunkComponent-only entity のサイレントスキップを可視化。
                //   保存データ上で ChunkComponent セクションに現れた pid のうち、
                //   ロード後の persistentToEntity に Entity が存在しないものをカウントし、
                //   1 セクションあたり 1 警告にまとめて通知する。
                //   （pid 単位で大量に警告するとノイズになるため）
                if (missingCount > 0)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.ChunkComponentEntityMissing,
                        typeSectionId: (uint)s,
                        saveEntityIndex: firstMissingPid,
                        sourceEntity: Entity.Null,
                        extraHash: (ulong)missingCount));
                }
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode RestoreSection(
            ByteView bodyView,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            SaveTypeModuleChunkMarker marker,
            ref bool merged,
            out int missingEntityCount,
            out int firstMissingPid)
        {
            var v = ChunkComponentSectionReader.Read(bodyView);
            int groupCount = (int)v.Header.GroupCount;
            int stride = (int)v.Header.Stride;

            return marker.RestoreChunks(
                v, groupCount, stride, persistentToEntity, em, ref merged,
                out missingEntityCount, out firstMissingPid);
        }
    }
}
