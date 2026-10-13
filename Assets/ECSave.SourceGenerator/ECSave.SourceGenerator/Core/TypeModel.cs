using System;
using System.Collections.Generic;

namespace ECSave.SourceGenerator.Core
{
    internal sealed class TypeModel : IEquatable<TypeModel>
    {
        public string FullTypeName { get; }
        public int StructSize { get; }
        public IReadOnlyList<FieldModel> Fields { get; }

        internal TypeModel(string fullTypeName, int structSize, IReadOnlyList<FieldModel> fields)
        {
            FullTypeName = fullTypeName ?? string.Empty;
            StructSize = structSize;
            Fields = fields ?? Array.Empty<FieldModel>();
        }

        public bool Equals(TypeModel other)
        {
            if (other == null) return false;
            if (FullTypeName != other.FullTypeName) return false;
            if (StructSize != other.StructSize) return false;
            if (Fields.Count != other.Fields.Count) return false;
            for (int i = 0; i < Fields.Count; i++)
                if (!Fields[i].Equals(other.Fields[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as TypeModel);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + FullTypeName.GetHashCode();
                h = h * 31 + StructSize;
                for (int i = 0; i < Fields.Count; i++)
                    h = h * 31 + Fields[i].GetHashCode();
                return h;
            }
        }
    }
}
