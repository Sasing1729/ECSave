using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Helpers
{
    internal static class SymbolExtensions
    {
        public static bool ImplementsInterface(this INamedTypeSymbol type, string metadataName)
        {
            if (type == null) return false;
            foreach (var i in type.AllInterfaces)
                if (i.ToDisplayString() == metadataName) return true;
            return false;
        }

        public static bool HasAttribute(this ISymbol symbol, string metadataName)
        {
            if (symbol == null) return false;
            foreach (var a in symbol.GetAttributes())
            {
                if (a.AttributeClass?.ToDisplayString() == metadataName) return true;
            }
            return false;
        }

        public static string ToFullyQualifiedName(this INamedTypeSymbol type)
            => type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
    }
}
