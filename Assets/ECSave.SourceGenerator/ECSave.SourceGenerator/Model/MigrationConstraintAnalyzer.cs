using System.Collections.Generic;
using ECSave.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Model
{
    internal static class MigrationConstraintAnalyzer
    {
        public static bool Analyze(
            ITypeSymbol migrationType,
            Location location,
            List<Diagnostic> diagnostics)
        {
            if (migrationType == null || diagnostics == null) return false;

            var named = migrationType as INamedTypeSymbol;
            if (named == null) return false;

            bool ok = true;

            bool hasParameterlessCtor = false;
            foreach (var member in named.InstanceConstructors)
            {
                if (member.Parameters.Length == 0)
                {
                    hasParameterlessCtor = true;
                    break;
                }
            }
            if (!hasParameterlessCtor)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.MigrationNoDefaultCtor,
                    location,
                    migrationType.ToDisplayString()));
                ok = false;
            }

            if (TryExtractTargetTypes(named, out var oldType, out var newType))
            {
                if (HasForbiddenField(oldType))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.MigrationTypeInvalid,
                        location,
                        oldType.ToDisplayString()));
                    ok = false;
                }
                if (HasForbiddenField(newType))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.MigrationTypeInvalid,
                        location,
                        newType.ToDisplayString()));
                    ok = false;
                }
            }

            return ok;
        }

        public static bool TryExtractTargetTypes(
            INamedTypeSymbol migrationType,
            out ITypeSymbol oldType,
            out ITypeSymbol newType)
        {
            oldType = null;
            newType = null;

            if (migrationType == null) return false;

            foreach (var i in migrationType.AllInterfaces)
            {
                var name = i.ToDisplayString();
                if (name.StartsWith("ECSave.IComponentMigration<")
                    || name.StartsWith("ECSave.Internal.IComponentMigration<"))
                {
                    if (i.TypeArguments.Length >= 2)
                    {
                        oldType = i.TypeArguments[0];
                        newType = i.TypeArguments[1];
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasForbiddenField(ITypeSymbol type)
        {
            var named = type as INamedTypeSymbol;
            if (named == null) return false;
            if (named.IsGenericType) return true;

            var name = named.ToDisplayString();
            if (name == "Unity.Entities.Entity") return true;
            if (name.StartsWith("Unity.Collections.FixedString")) return true;
            if (name.StartsWith("Unity.Collections.FixedList")) return true;

            foreach (var member in named.GetMembers())
            {
                var f = member as IFieldSymbol;
                if (f == null || f.IsStatic) continue;
                if (HasForbiddenField(f.Type)) return true;
            }
            return false;
        }
    }
}
