using System;
using System.Collections.Generic;
using ECSave.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Discovery
{
    internal static class SaveScopeFinder
    {
        public static ScopeDescriptor CreateDescriptor(
            INamedTypeSymbol type,
            Compilation compilation)
        {
            if (type == null) return null;

            var registrations = new List<RegistrationRecord>();
            var diagnostics = new List<Diagnostic>();
            bool configureFound = false;

            IMethodSymbol configure = null;
            try
            {
                configure = FindConfigureMethod(type);
            }
            catch (Exception ex)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.AnalysisFailed,
                    SafeLocation(type),
                    type.ToDisplayString(),
                    "Configure method lookup: " + ex.GetType().Name));
            }

            if (configure != null)
            {
                configureFound = true;

                try
                {
                    var collected = ConfigureCallCollector.Collect(configure, compilation);
                    if (collected != null)
                        registrations.AddRange(collected);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.AnalysisFailed,
                        SafeLocation(type),
                        type.ToDisplayString(),
                        "Configure analysis: " + ex.GetType().Name));
                }
            }

            string ns;
            try
            {
                ns = type.ContainingNamespace != null && !type.ContainingNamespace.IsGlobalNamespace
                    ? type.ContainingNamespace.ToDisplayString()
                    : string.Empty;
            }
            catch
            {
                ns = string.Empty;
            }

            string fullName;
            try
            {
                fullName = type.ToDisplayString();
            }
            catch
            {
                fullName = type.Name ?? "<unknown>";
            }

            return new ScopeDescriptor(
                fullName,
                ns,
                type.Name,
                type.IsAbstract,
                configureFound,
                registrations,
                diagnostics);
        }

        private static Location SafeLocation(INamedTypeSymbol type)
        {
            try
            {
                if (type.Locations != null && type.Locations.Length > 0)
                    return type.Locations[0];
            }
            catch { }
            return Location.None;
        }

        private static IMethodSymbol FindConfigureMethod(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers(WellKnownTypes.ConfigureMethodName))
            {
                var m = member as IMethodSymbol;
                if (m != null && m.Parameters.Length == 1)
                    return m;
            }
            return null;
        }
    }
}
