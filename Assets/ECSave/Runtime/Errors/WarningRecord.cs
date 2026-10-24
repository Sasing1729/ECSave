using Unity.Entities;

namespace ECSave.Internal
{
    public struct WarningRecord
    {
        public byte Kind;
        public uint TypeSectionId;
        public int SaveEntityIndex;
        public Entity SourceEntity;
        public ulong ExtraHash;

        public LoadWarningKind KindEnum => (LoadWarningKind)Kind;

        public static WarningRecord Create(LoadWarningKind kind, uint typeSectionId,
            int saveEntityIndex, Entity sourceEntity, ulong extraHash = 0UL)
            => new WarningRecord
            {
                Kind = (byte)kind,
                TypeSectionId = typeSectionId,
                SaveEntityIndex = saveEntityIndex,
                SourceEntity = sourceEntity,
                ExtraHash = extraHash,
            };
    }
}
