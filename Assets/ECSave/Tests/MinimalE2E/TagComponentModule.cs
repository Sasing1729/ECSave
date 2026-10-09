using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Unity.Entities;

namespace Game.MinimalE2E
{
    internal sealed class TagComponentModule : SaveTypeModuleEnableable<TagComponent>
    {
        public const string TypeFullName = "Game.Domain.TagComponent";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = System.Array.Empty<string>();

        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public override SectionKind SectionKind => SectionKind.Component;
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            if (!em.HasComponent<TagComponent>(entity))
                em.AddComponent<TagComponent>(entity);
        }

        private static byte[] BuildInput()
        {
            var buf = new byte[2];
            buf[0] = 0;
            buf[1] = 0;
            return buf;
        }
    }
}
