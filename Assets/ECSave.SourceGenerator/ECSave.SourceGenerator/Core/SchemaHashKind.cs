namespace ECSave.SourceGenerator.Core
{
    internal enum SchemaHashKind : byte
    {
        None = 0,
        Bool = 1,
        Int8 = 2,
        Int16 = 3,
        Int32 = 4,
        Int64 = 5,
        UInt8 = 6,
        UInt16 = 7,
        UInt32 = 8,
        UInt64 = 9,
        Float32 = 10,
        Float64 = 11,
        Entity = 12,
        FixedString = 13,
        FixedList = 14,
        Raw = 15,
        Struct = 16,
    }
}
