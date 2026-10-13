using System.Collections.Generic;

namespace ECSave.SourceGenerator.Core
{
    internal static class SchemaHashInputBuilder
    {
        public static byte[] Build(TypePlan plan)
        {
            if (plan == null) return new byte[0];

            var buffer = new List<byte>(64 + plan.Fields.Count * 16);

            buffer.Add((byte)(plan.StructSize & 0xFF));
            buffer.Add((byte)((plan.StructSize >> 8) & 0xFF));

            for (int i = 0; i < plan.Fields.Count; i++)
            {
                var f = plan.Fields[i];

                buffer.Add((byte)f.ElementKind);
                buffer.Add((byte)(f.Size & 0xFF));
                buffer.Add((byte)((f.Size >> 8) & 0xFF));
                buffer.Add((byte)(f.Offset & 0xFF));
                buffer.Add((byte)((f.Offset >> 8) & 0xFF));
                buffer.Add(0);
                buffer.Add(0);
                buffer.Add(0);

                ulong eh = f.ElementHash;
                for (int b = 0; b < 8; b++)
                    buffer.Add((byte)((eh >> (b * 8)) & 0xFF));
            }

            return buffer.ToArray();
        }

        public static ulong ComputeHash(TypePlan plan)
        {
            var input = Build(plan);
            return SchemaHashCore.Hash(input);
        }
    }
}
