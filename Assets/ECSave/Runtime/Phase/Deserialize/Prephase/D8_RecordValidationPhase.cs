using ECSave.Format;

namespace ECSave.Internal
{
    internal static unsafe class D8_RecordValidationPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;

                ref var entry = ref ctx.SectionTable.Entries[i];

                ByteView bodyView;
                try
                {
                    bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                }
                catch (FormatException fe)
                {
                    return ErrorInfoMapper.Map(fe.Error);
                }

                var err = entry.Kind == SectionKind.ChunkComponent
                    ? ValidateChunkSection(bodyView, ctx.Header.LogicalEntityCount)
                    : ValidateEntityIndexRange(bodyView, ctx.Header.LogicalEntityCount);

                if (err != SaveLoadErrorCode.None) return err;
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode ValidateEntityIndexRange(
            ByteView bodyView, int logicalEntityCount)
        {
            try
            {
                var body = CommonSectionReader.Read(bodyView);
                int count = (int)body.Header.Count;
                uint limit = (uint)logicalEntityCount;

                for (int i = 0; i < count; i++)
                    if (body.EntityIndex[i] >= limit)
                        return SaveLoadErrorCode.ValidationFailed;

                return SaveLoadErrorCode.None;
            }
            catch (FormatException fe)
            {
                return ErrorInfoMapper.Map(fe.Error);
            }
        }

        private static SaveLoadErrorCode ValidateChunkSection(
            ByteView bodyView, int logicalEntityCount)
        {
            try
            {
                var v = ChunkComponentSectionReader.Read(bodyView);
                int groupCount = (int)v.Header.GroupCount;
                int memberCount = (int)v.MemberOffset[groupCount];
                uint limit = (uint)logicalEntityCount;

                if (memberCount == 0) return SaveLoadErrorCode.None;

                if (logicalEntityCount > 0)
                {
                    int bitmapBytes = (logicalEntityCount + 31) / 32;
                    var bitmap = new uint[bitmapBytes];

                    for (int i = 0; i < memberCount; i++)
                    {
                        uint pid = v.MemberEntityIndex[i];
                        if (pid >= limit)
                            return SaveLoadErrorCode.ValidationFailed;

                        int word = (int)pid / 32;
                        uint mask = 1u << ((int)pid % 32);
                        if ((bitmap[word] & mask) != 0)
                            return SaveLoadErrorCode.ValidationFailed;
                        bitmap[word] |= mask;
                    }
                }

                return SaveLoadErrorCode.None;
            }
            catch (FormatException fe)
            {
                return ErrorInfoMapper.Map(fe.Error);
            }
        }
    }
}
