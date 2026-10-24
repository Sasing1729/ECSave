using System.Text;
using Unity.Entities;

namespace ECSave
{
    public readonly struct LoadWarning
    {
        public LoadWarningKind Kind { get; }
        public Entity SourceEntity { get; }
        public string TypeName { get; }
        public int SaveEntityIndex { get; }
        public ulong ExtraHash { get; }

        public LoadWarning(LoadWarningKind kind, Entity sourceEntity, string typeName,
            int saveEntityIndex, ulong extraHash = 0UL)
        {
            Kind = kind;
            SourceEntity = sourceEntity;
            TypeName = typeName;
            SaveEntityIndex = saveEntityIndex;
            ExtraHash = extraHash;
        }

        public override string ToString()
        {
            var sb = new StringBuilder(128);
            sb.Append(Kind);
            sb.Append(": Type=");
            sb.Append(TypeName ?? "<null>");
            sb.Append(", Source=");
            sb.Append(SourceEntity);
            sb.Append(", SaveIndex=");
            sb.Append(SaveEntityIndex);
            if (ExtraHash != 0UL)
            {
                sb.Append(", Extra=0x");
                sb.Append(ExtraHash.ToString("X16"));
            }
            return sb.ToString();
        }
    }
}
