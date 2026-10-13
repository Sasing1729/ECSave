using System;

namespace ECSave.SourceGenerator.Core
{
    internal sealed class FieldModel : IEquatable<FieldModel>
    {
        public string Name { get; }
        public FieldKind Kind { get; }
        public SchemaHashKind ElementKind { get; }
        public int Size { get; }
        public int Offset { get; }
        public ulong ElementHash { get; }

        internal FieldModel(
            string name,
            FieldKind kind,
            SchemaHashKind elementKind,
            int size,
            int offset,
            ulong elementHash = 0)
        {
            Name = name ?? string.Empty;
            Kind = kind;
            ElementKind = elementKind;
            Size = size;
            Offset = offset;
            ElementHash = elementHash;
        }

        public bool Equals(FieldModel other)
        {
            if (other == null) return false;
            return Name == other.Name
                && Kind == other.Kind
                && ElementKind == other.ElementKind
                && Size == other.Size
                && Offset == other.Offset
                && ElementHash == other.ElementHash;
        }

        public override bool Equals(object obj) => Equals(obj as FieldModel);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Name?.GetHashCode() ?? 0);
                h = h * 31 + (int)Kind;
                h = h * 31 + (int)ElementKind;
                h = h * 31 + Size;
                h = h * 31 + Offset;
                h = h * 31 + ElementHash.GetHashCode();
                return h;
            }
        }
    }
}
