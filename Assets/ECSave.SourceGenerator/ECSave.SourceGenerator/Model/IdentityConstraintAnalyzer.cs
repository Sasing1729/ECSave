using System.Collections.Generic;
using ECSave.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Model
{
    internal static class IdentityConstraintAnalyzer
    {
        public const int MaxFields = 32;
        public const int MaxNestingDepth = 4;

        public static bool Analyze(
            ITypeSymbol type,
            Location location,
            List<Diagnostic> diagnostics)
        {
            if (type == null || diagnostics == null) return false;

            bool ok = true;

            bool isComponent = Implements(type, "Unity.Entities.IComponentData");
            bool isBuffer = Implements(type, "Unity.Entities.IBufferElementData");
            bool isShared = Implements(type, "Unity.Entities.ISharedComponentData");

            if (!isComponent)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentityNotComponent,
                    location,
                    type.ToDisplayString()));
                ok = false;
            }

            if (isBuffer)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentityBufferNotAllowed,
                    location,
                    type.ToDisplayString()));
                ok = false;
            }

            if (isShared)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentitySharedNotAllowed,
                    location,
                    type.ToDisplayString()));
                ok = false;
            }

            var named = type as INamedTypeSymbol;
            if (named == null) return ok;

            if (named.TypeParameters.Length > 0 || named.IsGenericType)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentityGenericNotAllowed,
                    location,
                    type.ToDisplayString()));
                ok = false;
            }

            var layout = DetectLayoutKind(named);
            if (layout == LayoutKind.Explicit)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentityExplicitNotAllowed,
                    location,
                    type.ToDisplayString()));
                ok = false;
            }

            int fieldCount = 0;
            CountFields(named, ref fieldCount);

            if (fieldCount > MaxFields)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentityTooManyFields,
                    location,
                    type.ToDisplayString(),
                    fieldCount));
                ok = false;
            }

            int depth = ComputeNestingDepth(named, 0);
            if (depth > MaxNestingDepth)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentityTooDeep,
                    location,
                    type.ToDisplayString()));
                ok = false;
            }

            if (ContainsEntity(named))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.IdentityEntityNotAllowed,
                    location,
                    type.ToDisplayString()));
                ok = false;
            }

            return ok;
        }

        private static bool Implements(ITypeSymbol type, string metadataName)
        {
            foreach (var i in type.AllInterfaces)
                if (i.ToDisplayString() == metadataName) return true;
            return false;
        }

        private static void CountFields(INamedTypeSymbol type, ref int count)
        {
            foreach (var member in type.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null || f.IsStatic) continue;
                count++;
            }
        }

        private static int ComputeNestingDepth(INamedTypeSymbol type, int depth)
        {
            if (type == null || depth > MaxNestingDepth + 1) return depth;

            int max = depth;
            foreach (var member in type.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null || f.IsStatic) continue;

                var nested = f.Type as INamedTypeSymbol;
                if (nested == null) continue;
                if (IsPrimitive(nested)) continue;
                if (nested.ToDisplayString() == "Unity.Entities.Entity") continue;

                int child = ComputeNestingDepth(nested, depth + 1);
                if (child > max) max = child;
            }
            return max;
        }

        private static bool ContainsEntity(INamedTypeSymbol type)
        {
            if (type == null) return false;

            foreach (var member in type.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null || f.IsStatic) continue;

                if (f.Type.ToDisplayString() == "Unity.Entities.Entity")
                    return true;

                var nested = f.Type as INamedTypeSymbol;
                if (nested != null && ContainsEntity(nested))
                    return true;
            }
            return false;
        }

        private static bool IsPrimitive(INamedTypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return true;
                default:
                    return false;
            }
        }

        private enum LayoutKind { Sequential, Explicit, Auto }

        private static LayoutKind DetectLayoutKind(INamedTypeSymbol type)
        {
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() !=
                    "System.Runtime.InteropServices.StructLayoutAttribute")
                    continue;
                if (attr.ConstructorArguments.Length < 1) continue;

                var v = attr.ConstructorArguments[0].Value;
                if (v == null) continue;

                int value = System.Convert.ToInt32(v);
                if (value == 2) return LayoutKind.Explicit;
                if (value == 3) return LayoutKind.Auto;
                return LayoutKind.Sequential;
            }
            return LayoutKind.Sequential;
        }
    }
}
