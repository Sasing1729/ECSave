using System.Collections.Generic;
using ECSave.SourceGenerator.Core;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Model
{
    internal sealed class TypeAnalysisResult
    {
        public TypePlan Plan { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public TypeAnalysisResult(TypePlan plan, IReadOnlyList<Diagnostic> diagnostics)
        {
            Plan = plan;
            Diagnostics = diagnostics ?? new List<Diagnostic>();
        }
    }

    internal static class TypeAnalyzer
    {
        public static TypeAnalysisResult Analyze(INamedTypeSymbol type)
        {
            var diagnostics = new List<Diagnostic>();
            if (type == null)
                return new TypeAnalysisResult(null, diagnostics);

            var layoutKind = DetectLayoutKind(type);
            bool hasEntity = ContainsEntity(type);

            if (layoutKind == LayoutKind.Auto)
            {
                if (hasEntity)
                    diagnostics.Add(Diagnostic.Create(
                        Analyzers.DiagnosticDescriptors.SaveTypeAutoWithEntity,
                        type.Locations.Length > 0 ? type.Locations[0] : Location.None,
                        type.ToDisplayString()));
                else
                    diagnostics.Add(Diagnostic.Create(
                        Analyzers.DiagnosticDescriptors.SaveTypeAutoWithoutEntity,
                        type.Locations.Length > 0 ? type.Locations[0] : Location.None,
                        type.ToDisplayString()));
            }

            if (HasPackAttribute(type))
            {
                diagnostics.Add(Diagnostic.Create(
                    Analyzers.DiagnosticDescriptors.SaveTypePackNotSupported,
                    type.Locations.Length > 0 ? type.Locations[0] : Location.None,
                    type.ToDisplayString()));
            }

            var fields = new List<FieldPlan>();
            var refOffsets = new List<int>();

            int offset = 0;
            int maxAlign = 1;

            foreach (var member in type.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null) continue;
                if (f.IsStatic) continue;
                if (f.IsConst) continue;

                if (f.DeclaredAccessibility != Accessibility.Public
                    && f.DeclaredAccessibility != Accessibility.Internal)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Analyzers.DiagnosticDescriptors.SaveTypePrivateField,
                        f.Locations.Length > 0 ? f.Locations[0] : Location.None,
                        type.ToDisplayString(),
                        f.Name));
                }

                var plan = AnalyzeField(f, ref offset, ref maxAlign, refOffsets, diagnostics, type);
                if (plan != null)
                    fields.Add(plan);
            }

            int structSize = AlignUp(offset, maxAlign);

            var refArray = refOffsets.ToArray();
            int recordStride = structSize - 4 * refArray.Length;
            if (recordStride < 0) recordStride = 0;

            var typePlan = new TypePlan(
                type.ToDisplayString(),
                structSize,
                fields,
                refArray,
                recordStride);

            return new TypeAnalysisResult(typePlan, diagnostics);
        }

        private static FieldPlan AnalyzeField(
            IFieldSymbol f,
            ref int offset,
            ref int maxAlign,
            List<int> refOffsets,
            List<Diagnostic> diagnostics,
            INamedTypeSymbol owner)
        {
            var type = f.Type;
            if (type == null) return null;

            if (IsEntityType(type))
            {
                int align = AlignOf(type);
                int fieldOffset = AlignUp(offset, align);
                offset = fieldOffset + 8;

                if (align > maxAlign) maxAlign = align;
                refOffsets.Add(fieldOffset);

                return new FieldPlan(
                    f.Name,
                    FieldKind.Entity,
                    SchemaHashKind.Entity,
                    8,
                    fieldOffset,
                    0,
                    isEntityRef: true);
            }

            if (PrimitiveKindMapper.TryMap(type, out var kind, out var primSize))
            {
                int align = PrimitiveAlign(primSize);
                int fieldOffset = AlignUp(offset, align);
                offset = fieldOffset + primSize;

                if (align > maxAlign) maxAlign = align;

                return new FieldPlan(
                    f.Name,
                    FieldKind.Primitive,
                    kind,
                    primSize,
                    fieldOffset);
            }

            int size = EstimateSize(type);
            int nestedAlign = AlignOf(type);
            int nestedOffset = AlignUp(offset, nestedAlign);
            offset = nestedOffset + size;

            if (nestedAlign > maxAlign) maxAlign = nestedAlign;

            var nested = type as INamedTypeSymbol;
            if (nested != null)
            {
                return new FieldPlan(
                    f.Name,
                    FieldKind.Nested,
                    SchemaHashKind.Struct,
                    size,
                    nestedOffset,
                    0);
            }

            return new FieldPlan(
                f.Name,
                FieldKind.Raw,
                SchemaHashKind.Raw,
                size,
                nestedOffset);
        }

        private static bool ContainsEntity(ITypeSymbol type)
        {
            if (type == null) return false;
            if (IsEntityType(type)) return true;

            var named = type as INamedTypeSymbol;
            if (named == null) return false;

            foreach (var member in named.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null || f.IsStatic) continue;
                if (ContainsEntity(f.Type)) return true;
            }
            return false;
        }

        private static bool IsEntityType(ITypeSymbol type)
            => type != null
               && type.ToDisplayString() == Discovery.WellKnownTypes.EntityMetadataName;

        private static int EstimateSize(ITypeSymbol type)
        {
            if (type == null) return 0;

            if (IsEntityType(type)) return 8;

            if (PrimitiveKindMapper.TryMap(type, out _, out var size))
                return size;

            var t = type as INamedTypeSymbol;
            if (t == null) return 0;

            int offset = 0;
            int maxAlign = 1;

            foreach (var member in t.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null || f.IsStatic) continue;

                int align = AlignOf(f.Type);
                int fsize = EstimateSize(f.Type);

                offset = AlignUp(offset, align);
                offset += fsize;

                if (align > maxAlign) maxAlign = align;
            }

            return AlignUp(offset, maxAlign);
        }

        private static int AlignOf(ITypeSymbol type)
        {
            if (type == null) return 1;

            if (IsEntityType(type)) return 4;

            if (type.TypeKind == TypeKind.Enum)
            {
                var named = type as INamedTypeSymbol;
                if (named != null && named.EnumUnderlyingType != null)
                    return AlignOf(named.EnumUnderlyingType);
                return 4;
            }

            if (PrimitiveKindMapper.TryMap(type, out _, out var size))
                return PrimitiveAlign(size);

            var t = type as INamedTypeSymbol;
            if (t == null) return 1;

            int maxAlign = 1;
            foreach (var member in t.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null || f.IsStatic) continue;
                int a = AlignOf(f.Type);
                if (a > maxAlign) maxAlign = a;
            }
            return maxAlign;
        }

        private static int PrimitiveAlign(int size)
        {
            if (size >= 8) return 8;
            if (size >= 4) return 4;
            if (size >= 2) return 2;
            return 1;
        }

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1) return value;
            int rem = value % alignment;
            return rem == 0 ? value : value + (alignment - rem);
        }

        private enum LayoutKind
        {
            Sequential = 0,
            Explicit = 1,
            Auto = 2,
        }

        private static LayoutKind DetectLayoutKind(INamedTypeSymbol type)
        {
            if (type == null) return LayoutKind.Sequential;

            foreach (var attr in type.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass == null) continue;
                if (attrClass.ToDisplayString() != "System.Runtime.InteropServices.StructLayoutAttribute")
                    continue;

                if (attr.ConstructorArguments.Length < 1) continue;
                var arg = attr.ConstructorArguments[0];
                if (arg.Value == null) continue;

                int value = System.Convert.ToInt32(arg.Value);
                if (value == 2) return LayoutKind.Explicit;
                if (value == 3) return LayoutKind.Auto;
                return LayoutKind.Sequential;
            }

            return LayoutKind.Sequential;
        }

        private static bool HasPackAttribute(INamedTypeSymbol type)
        {
            if (type == null) return false;

            foreach (var attr in type.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass == null) continue;
                if (attrClass.ToDisplayString() != "System.Runtime.InteropServices.StructLayoutAttribute")
                    continue;

                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "Pack")
                        return true;
                }
            }
            return false;
        }
    }
}
