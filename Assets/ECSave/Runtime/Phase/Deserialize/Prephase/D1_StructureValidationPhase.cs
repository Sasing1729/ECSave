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
