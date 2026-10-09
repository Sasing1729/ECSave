using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Unity.Entities;

namespace Game.MinimalE2E
{
    internal sealed class StunnedModule : SaveTypeModuleEnableable<Stunned>
    {
        public const string TypeFullName = "Game.Domain.Stunned";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = { "FramesRemaining" };

        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public override SectionKind SectionKind => SectionKind.Component;
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        public override EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Stunned>() },
                // A-06：IgnoreComponentEnabledState を指定。無効 Entity も対象。
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            var s = new Stunned { FramesRemaining = *(int*)value };
            if (em.HasComponent<Stunned>(entity))
                em.SetComponentData(entity, s);
            else
                em.AddComponentData(entity, s);
        }

        private unsafe static byte[] BuildInput()
        {
            var buf = new byte[2 + 16];
            buf[0] = (byte)(sizeof(Stunned) & 0xFF);
            buf[1] = (byte)(sizeof(Stunned) >> 8);
            WriteEntry(buf, 2, SchemaHashKind.Int32, 4, 0);
            return buf;
        }

        private static void WriteEntry(
            byte[] buf, int offset, SchemaHashKind kind, ushort size, ushort fieldOffset)
        {
            buf[offset + 0] = (byte)kind;
            buf[offset + 1] = (byte)(size & 0xFF);
            buf[offset + 2] = (byte)(size >> 8);
            buf[offset + 3] = (byte)(fieldOffset & 0xFF);
            buf[offset + 4] = (byte)(fieldOffset >> 8);
        }
    }
}
