// Runtime/Public/LoadWarning.cs
using Unity.Entities;

namespace ECSave
{
    /// <summary>ロード処理中の警告（外部設計 13.4）。</summary>
    public readonly struct LoadWarning
    {
        public LoadWarningKind Kind { get; }
        public Entity SourceEntity { get; }
        public string TypeName { get; }
        public int SaveEntityIndex { get; }

        public LoadWarning(LoadWarningKind kind, Entity sourceEntity, string typeName, int saveEntityIndex)
        {
            Kind = kind;
            SourceEntity = sourceEntity;
            TypeName = typeName;
            SaveEntityIndex = saveEntityIndex;
        }

        public override string ToString()
            => $"{Kind}: Type={TypeName ?? "<null>"}, Source={SourceEntity}, SaveIndex={SaveEntityIndex}";
    }
}
