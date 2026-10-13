namespace ECSave.SourceGenerator.Core
{
    internal enum FieldKind : byte
    {
        Unknown = 0,
        Primitive = 1,
        Entity = 2,
        FixedString = 3,
        FixedList = 4,
        Nested = 5,
        Raw = 6,
    }
}
