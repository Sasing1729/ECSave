using System.Collections.Generic;
using System.Text;

namespace ECSave.SourceGenerator.Core
{
    internal static class SchemaHasher
    {
        public static ulong ComputeHash(TypeModel model)
        {
            if (model == null) return SchemaHashCore.FnvOffsetBasis;

            var buffer = new List<byte>(64 + model.Fields.Count * 16);

            buffer.Add((byte)(model.StructSize & 0xFF));
            buffer.Add((byte)((model.StructSize >> 8) & 0xFF));

            for (int i = 0; i < model.Fields.Count; i++)
            {
                var f = model.Fields[i];

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

            return SchemaHashCore.Hash(buffer.ToArray());
        }
    }
}
