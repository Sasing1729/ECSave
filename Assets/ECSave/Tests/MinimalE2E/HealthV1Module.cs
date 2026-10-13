using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Unity.Entities;

namespace Game.MinimalE2E
{
    internal sealed class HealthV1Module : SaveTypeModulePlain<HealthV1>
    {
        public static readonly string Full = "Game.Domain.Health";
        public static readonly byte[] Input = Build();
        public static readonly ulong Hash = SchemaHashCore.Hash(Input);
        public static readonly string[] Names = { "Value" };

        public override string FullTypeName => Full;
        public override SectionKind SectionKind => SectionKind.Component;
        public override ulong SchemaHash => Hash;
        public override byte[] SchemaHashInput => Input;
        public override string[] FieldNames => Names;

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            var v = new HealthV1 { Value = *(int*)value };
            if (em.HasComponent<HealthV1>(entity))
                em.SetComponentData(entity, v);
            else
                em.AddComponentData(entity, v);
        }

        private unsafe static byte[] Build()
        {
            var b = new byte[2 + 16];
            b[0] = (byte)(sizeof(HealthV1) & 0xFF);
            b[1] = (byte)(sizeof(HealthV1) >> 8);
            b[2] = (byte)SchemaHashKind.Int32;
            b[3] = 4;
            b[4] = 0;
            return b;
        }
    }
}
