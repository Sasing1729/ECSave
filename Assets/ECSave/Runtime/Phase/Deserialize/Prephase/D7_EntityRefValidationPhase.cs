using ECSave.Format;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class D7_EntityRefValidationPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            uint limit = (uint)ctx.Header.LogicalEntityCount;

            for (int s = 0; s < ctx.SectionTable.Count; s++)
            {
                var m = ctx.ModuleBySection[s];
                if (m == null) continue;

                var refs = m.EntityRefOffsets;
                if (refs == null || refs.Length == 0) continue;

                ByteView bodyView;
                try
                {
                    bodyView = SectionBodyHelper.GetSectionBody(ctx, s);
                }
                catch (FormatException)
                {
                    continue;
                }

                if (m.SectionKind == SectionKind.ChunkComponent)
                {
                    var err = ValidateChunkSection(
                        bodyView, refs, limit, (uint)s, ref ctx);
                    if (err != SaveLoadErrorCode.None) return err;
                }
                else
                {
                    var err = ValidateCommonSection(
                        bodyView, refs, limit, (uint)s, ref ctx);
                    if (err != SaveLoadErrorCode.None) return err;
                }
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode ValidateCommonSection(
            ByteView bodyView, int[] refs, uint limit, uint sectionId,
            ref PrephaseContext ctx)
        {
            var v = CommonSectionReader.Read(bodyView);
            int count = (int)v.Header.Count;
            int stride = (int)v.Header.Stride;
            if (stride <= 0) return SaveLoadErrorCode.None;

            if (v.Header.IsFixedLength)
            {
                for (int i = 0; i < count; i++)
                {
                    byte* rec = v.Records + i * stride;
                    var err = CheckRecord(
                        rec, stride, refs, limit, sectionId, v.EntityIndex[i], ref ctx);
                    if (err != SaveLoadErrorCode.None) return err;
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    uint start = v.RecordOffset[i];
                    uint end = v.RecordOffset[i + 1];
                    int elemCount = (int)((end - start) / (uint)stride);
                    for (int k = 0; k < elemCount; k++)
                    {
                        byte* rec = v.Records + start + k * stride;
                        var err = CheckRecord(
                            rec, stride, refs, limit, sectionId, v.EntityIndex[i], ref ctx);
                        if (err != SaveLoadErrorCode.None) return err;
                    }
                }
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode ValidateChunkSection(
            ByteView bodyView, int[] refs, uint limit, uint sectionId,
            ref PrephaseContext ctx)
        {
            var v = ChunkComponentSectionReader.Read(bodyView);
            int groupCount = (int)v.Header.GroupCount;
            int stride = (int)v.Header.Stride;  // dataStride
            if (stride <= 0) return SaveLoadErrorCode.None;

            for (int g = 0; g < groupCount; g++)
            {
                int mStart = (int)v.MemberOffset[g];
                int mEnd = (int)v.MemberOffset[g + 1];

                // 空グループはスキップ（Capture で count>0 の保証はあるが、
                // 破損データへの防御として P7.2 と同じパターンでガード）。
                if (mStart >= mEnd) continue;

                byte* rec = v.Records + g * stride;

                // selfPid: 代表メンバーの pid（P6_5 / P7.2 と一致）
                uint selfPid = v.MemberEntityIndex[mStart];

                var err = CheckRecord(
                    rec, stride, refs, limit, sectionId, selfPid, ref ctx);
                if (err != SaveLoadErrorCode.None) return err;
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode CheckRecord(
            byte* rec, int stride, int[] refs, uint limit,
            uint sectionId, uint selfPid, ref PrephaseContext ctx)
        {
            for (int r = 0; r < refs.Length; r++)
            {
                int pidOffset = refs[r] - 4 * r;
                if (pidOffset < 0 || pidOffset + 4 > stride) continue;

                uint pid = *(uint*)(rec + pidOffset);

                if (pid == EntityRefConstants.NullRef) continue;

                if (pid == EntityRefConstants.UnresolvedRef)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.EntityReferenceUnresolved,
                        sectionId, (int)selfPid, Entity.Null));
                    continue;
                }

                if (pid >= limit)
                    return SaveLoadErrorCode.EntityReferenceInvalid;
            }

            return SaveLoadErrorCode.None;
        }
    }
}
