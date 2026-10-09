// Runtime/Errors/WarningRecord.cs
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// Burst ジョブ内で記録する警告（15.4節）。文字列を持たない。
    /// </summary>
    public struct WarningRecord
    {
        public byte Kind;           // LoadWarningKind を byte 化
        public uint TypeSectionId;
        public int SaveEntityIndex;
        public Entity SourceEntity;

        public LoadWarningKind KindEnum => (LoadWarningKind)Kind;

        public static WarningRecord Create(LoadWarningKind kind, uint typeSectionId,
            int saveEntityIndex, Entity sourceEntity)
            => new WarningRecord
            {
                Kind = (byte)kind,
                TypeSectionId = typeSectionId,
                SaveEntityIndex = saveEntityIndex,
                SourceEntity = sourceEntity,
            };
    }
}
