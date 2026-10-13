using System;

namespace ECSave.SourceGenerator.Core
{
    internal sealed class FieldPlan : IEquatable<FieldPlan>
    {
        public string Name { get; }
        public FieldKind Kind { get; }
        public SchemaHashKind ElementKind { get; }
        public int Size { get; }
        public int Offset { get; }
        public ulong ElementHash { get; }
        public bool IsEntityRef { get; }

        public FieldPlan(
            string name,
            FieldKind kind,
            SchemaHashKind elementKind,
            int size,
            int offset,
            ulong elementHash = 0,
            bool isEntityRef = false)
        {
            Name = name ?? string.Empty;
            Kind = kind;
            ElementKind = elementKind;
            Size = size;
            Offset = offset;
            ElementHash = elementHash;
            IsEntityRef = isEntityRef;
        }

        public bool Equals(FieldPlan other)
        {
            if (other == null) return false;
            return Name == other.Name
                && Kind == other.Kind
                && ElementKind == other.ElementKind
                && Size == other.Size
                && Offset == other.Offset
                && ElementHash == other.ElementHash
                && IsEntityRef == other.IsEntityRef;
        }

        public override bool Equals(object obj) => Equals(obj as FieldPlan);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Name.GetHashCode();
                h = h * 31 + (int)Kind;
                h = h * 31 + (int)ElementKind;
                h = h * 31 + Size;
                h = h * 31 + Offset;
                h = h * 31 + ElementHash.GetHashCode();
                h = h * 31 + IsEntityRef.GetHashCode();
                return h;
            }
        }
    }
}
