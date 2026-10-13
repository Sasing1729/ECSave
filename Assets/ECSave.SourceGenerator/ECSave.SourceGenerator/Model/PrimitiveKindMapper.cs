using ECSave.SourceGenerator.Core;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Model
{
    internal static class PrimitiveKindMapper
    {
        public static bool TryMap(ITypeSymbol type, out SchemaHashKind kind, out int size)
        {
            if (type == null)
            {
                kind = SchemaHashKind.None;
                size = 0;
                return false;
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                var named = type as INamedTypeSymbol;
                if (named != null && named.EnumUnderlyingType != null)
                    return TryMap(named.EnumUnderlyingType, out kind, out size);
                kind = SchemaHashKind.Int32;
                size = 4;
                return true;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: kind = SchemaHashKind.Bool; size = 1; return true;
                case SpecialType.System_SByte: kind = SchemaHashKind.Int8; size = 1; return true;
                case SpecialType.System_Int16: kind = SchemaHashKind.Int16; size = 2; return true;
                case SpecialType.System_Int32: kind = SchemaHashKind.Int32; size = 4; return true;
                case SpecialType.System_Int64: kind = SchemaHashKind.Int64; size = 8; return true;
                case SpecialType.System_Byte: kind = SchemaHashKind.UInt8; size = 1; return true;
                case SpecialType.System_UInt16: kind = SchemaHashKind.UInt16; size = 2; return true;
                case SpecialType.System_UInt32: kind = SchemaHashKind.UInt32; size = 4; return true;
                case SpecialType.System_UInt64: kind = SchemaHashKind.UInt64; size = 8; return true;
                case SpecialType.System_Single: kind = SchemaHashKind.Float32; size = 4; return true;
                case SpecialType.System_Double: kind = SchemaHashKind.Float64; size = 8; return true;
                default:
                    kind = SchemaHashKind.None;
                    size = 0;
                    return false;
            }
        }

        public static bool IsPrimitive(ITypeSymbol type)
            => TryMap(type, out _, out _);
    }
}
