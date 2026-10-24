using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Unity.Entities;

namespace Game.MinimalE2E
{
    internal sealed class HealthModule : SaveTypeModulePlain<Health>
    {
        public const string TypeFullName = "Game.Domain.Health";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = { "Current", "Max" };

        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public override SectionKind SectionKind => SectionKind.Component;
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        public override EntityQuery BuildQuery(EntityManager em)
            => ModuleQueryCache.GetOrCreate(em, this, () =>
                em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Health>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState
                            | EntityQueryOptions.IncludeDisabledEntities,
                }));

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            var h = new Health
            {
                Current = *(int*)value,
                Max = *(int*)(value + 4),
            };
            if (em.HasComponent<Health>(entity))
                em.SetComponentData(entity, h);
            else
                em.AddComponentData(entity, h);
        }

        private unsafe static byte[] BuildInput()
        {
            var buf = new byte[2 + 16 + 16];
            buf[0] = (byte)(sizeof(Health) & 0xFF);
            buf[1] = (byte)(sizeof(Health) >> 8);
            WriteEntry(buf, 2, SchemaHashKind.Int32, 4, 0);
            WriteEntry(buf, 18, SchemaHashKind.Int32, 4, 4);
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
