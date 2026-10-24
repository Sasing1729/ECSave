using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class D1_StructureValidationPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            try
            {
                var reader = new FormatReader(ctx.View);
                ctx.Header = reader.ReadHeader();

                // Review #12c: チェックサム検証を LogicalEntityCount×8B（最大 80MB）の
                // 確保より前に行う。旧実装は D2 が D1 の後だったため、チェックサム不正
                // でも先に 80MB を確保していた（敵対的入力で DoS 可能）。
                int total = (int)ctx.Header.TotalLength;
                int checksumEnd = total - ECSaveFormatConstants.FooterSize;
                byte* p = ctx.View.Pointer;
                ulong stored = *(ulong*)(p + checksumEnd);
                ulong actual = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));
                if (stored != actual)
                    return SaveLoadErrorCode.ChecksumMismatch;

                ctx.SectionTable = reader.ReadSectionTable();

                StructureValidator.ValidateNoOverlap(
                    ctx.SectionTable, ctx.Header.TotalLength);

                int entityCount = ctx.Header.LogicalEntityCount;
                ctx.PersistentToEntity = new NativeArray<Entity>(entityCount, Allocator.TempJob);
                for (int i = 0; i < entityCount; i++)
                    ctx.PersistentToEntity[i] = Entity.Null;

                return SaveLoadErrorCode.None;
            }
            catch (FormatException fe)
            {
                return ErrorInfoMapper.Map(fe.Error);
            }
        }
    }
}
