using System;
using System.Collections.Generic;

namespace ECSave.SourceGenerator.Core
{
    internal sealed class TypePlan : IEquatable<TypePlan>
    {
        public string FullTypeName { get; }
        public int StructSize { get; }
        public IReadOnlyList<FieldPlan> Fields { get; }
        public int[] EntityRefOffsets { get; }
        public int RecordStride { get; }

        public TypePlan(
            string fullTypeName,
            int structSize,
            IReadOnlyList<FieldPlan> fields,
            int[] entityRefOffsets,
            int recordStride)
        {
            FullTypeName = fullTypeName ?? string.Empty;
            StructSize = structSize;
            Fields = fields ?? Array.Empty<FieldPlan>();
            EntityRefOffsets = entityRefOffsets ?? Array.Empty<int>();
            RecordStride = recordStride;
        }

        public bool Equals(TypePlan other)
        {
            if (other == null) return false;
            if (FullTypeName != other.FullTypeName) return false;
            if (StructSize != other.StructSize) return false;
            if (RecordStride != other.RecordStride) return false;
            if (Fields.Count != other.Fields.Count) return false;
            for (int i = 0; i < Fields.Count; i++)
                if (!Fields[i].Equals(other.Fields[i])) return false;
            if (EntityRefOffsets.Length != other.EntityRefOffsets.Length) return false;
            for (int i = 0; i < EntityRefOffsets.Length; i++)
                if (EntityRefOffsets[i] != other.EntityRefOffsets[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as TypePlan);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + FullTypeName.GetHashCode();
                h = h * 31 + StructSize;
                h = h * 31 + RecordStride;
                for (int i = 0; i < Fields.Count; i++)
                    h = h * 31 + Fields[i].GetHashCode();
                return h;
            }
        }
    }
}
