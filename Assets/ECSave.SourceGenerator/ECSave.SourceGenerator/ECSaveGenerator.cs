using System.Collections.Generic;
using System.Text;
using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Emission;
using ECSave.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ECSave.SourceGenerator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ECSaveGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var scopes = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, ct) =>
                    {
                        var classDecl = ctx.Node as ClassDeclarationSyntax;
                        if (classDecl == null) return null;

                        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
                        if (symbol == null) return null;
                        if (!WellKnownTypes.IsSaveScope(symbol)) return null;

                        return SaveScopeFinder.CreateDescriptor(symbol, ctx.SemanticModel.Compilation);
                    })
                .Where(static d => d is not null)
                .Collect();

            context.RegisterSourceOutput(scopes, static (spc, descriptors) =>
            {
                if (descriptors.Length == 0) return;

                EmitContractVersion(spc);
                EmitScopeRegistry(spc, descriptors);
                EmitModules(spc, descriptors);
                EmitBindings(spc, descriptors);
                EmitSelfChecks(spc, descriptors);
                EmitSelections(spc, descriptors);
            });
        }

        private static void EmitContractVersion(SourceProductionContext spc)
        {
            spc.AddSource("ECSaveContractVersion.g.cs",
                SourceText.From(
                    "namespace ECSave.Generated\n" +
                    "{\n" +
                    "    public static class GeneratorContractVersion\n" +
                    "    {\n" +
                    "        public const int Value = 1;\n" +
                    "    }\n" +
                    "}\n",
                    Encoding.UTF8));
        }

        private static void EmitScopeRegistry(
            SourceProductionContext spc,
            System.Collections.Immutable.ImmutableArray<ScopeDescriptor> descriptors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("namespace ECSave.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ECSaveScopeRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        public static readonly string[] ScopeNames = new string[]");
            sb.AppendLine("        {");

            foreach (var d in descriptors)
            {
                if (d == null) continue;
                sb.Append("            \"").Append(d.FullTypeName).AppendLine("\",");
            }

            sb.AppendLine("        };");
            sb.AppendLine();

            sb.AppendLine("        public static readonly int[] RegistrationCounts = new int[]");
            sb.AppendLine("        {");

            foreach (var d in descriptors)
            {
                if (d == null) continue;
                sb.Append("            ").Append(d.Registrations.Count).AppendLine(",");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            spc.AddSource("ECSaveScopeRegistry.g.cs",
                SourceText.From(sb.ToString(), Encoding.UTF8));

            for (int i = 0; i < descriptors.Length; i++)
            {
                var d = descriptors[i];
                if (d?.Diagnostics == null) continue;
                for (int j = 0; j < d.Diagnostics.Count; j++)
                {
                    var diag = d.Diagnostics[j];
                    if (diag != null) spc.ReportDiagnostic(diag);
                }
            }
        }

        private static void EmitModules(
            SourceProductionContext spc,
            System.Collections.Immutable.ImmutableArray<ScopeDescriptor> descriptors)
        {
            var emittedModules = new HashSet<string>();
            var emittedDiagnostics = new HashSet<string>();
            var migrationStepEmitted = new HashSet<string>();

            foreach (var scope in descriptors)
            {
                if (scope == null) continue;

                foreach (var reg in scope.Registrations)
                {
                    EmitDiagnostics(spc, reg, emittedDiagnostics);

                    if (string.IsNullOrEmpty(reg.TypeArgumentFullName)) continue;

                    if (reg.Kind == RegistrationKind.Migration)
                    {
                        EmitMigrationStep(spc, reg, migrationStepEmitted);
                        continue;
                    }

                    if (reg.ClassifiedAs == ModuleClass.Unknown) continue;

                    var key = reg.ClassifiedAs.ToString() + "|" + reg.TypeArgumentFullName;
                    if (!emittedModules.Add(key)) continue;

                    if (reg.ClassifiedAs == ModuleClass.Identity)
                    {
                        var text = IdentityModuleEmitter.Emit(
                            reg.TypeArgumentFullName,
                            reg.Plan,
                            reg.SchemaHashInput,
                            reg.SchemaHash);

                        var fileName = TypeNameSanitizer.ToShortIdentifier(reg.TypeArgumentFullName)
                            + "IdentityModule.g.cs";
                        spc.AddSource(fileName, SourceText.From(text, Encoding.UTF8));
                        continue;
                    }

                    if (!TypeModuleClassifier.IsModuleClass(reg.ClassifiedAs)) continue;

                    var baseName = TypeModuleClassifier.ToBaseClassName(reg.ClassifiedAs);
                    if (baseName == null) continue;

                    var moduleText = ModuleEmitter.Emit(
                        reg.TypeArgumentFullName,
                        baseName,
                        reg.Plan,
                        reg.SchemaHashInput,
                        reg.SchemaHash,
                        reg.Filter,
                        reg.WithoutEnableableState);

                    var moduleFile = TypeNameSanitizer.ToShortIdentifier(reg.TypeArgumentFullName)
                        + "Module.g.cs";
                    spc.AddSource(moduleFile, SourceText.From(moduleText, Encoding.UTF8));
                }
            }
        }

        private static void EmitMigrationStep(
            SourceProductionContext spc,
            RegistrationRecord reg,
            HashSet<string> emitted)
        {
            var migrationType = reg.TypeArgumentFullName;
            if (string.IsNullOrEmpty(migrationType)) return;

            if (string.IsNullOrEmpty(reg.MigrationOldTypeFullName)) return;
            if (string.IsNullOrEmpty(reg.MigrationNewTypeFullName)) return;

            var key = "migration|" + migrationType;
            if (!emitted.Add(key)) return;

            var text = MigrationStepEmitter.Emit(
                migrationType,
                reg.MigrationOldTypeFullName,
                reg.MigrationNewTypeFullName,
                reg.MigrationOldPlan,
                reg.MigrationNewPlan,
                reg.MigrationNewTypeNameHash,
                reg.MigrationOldSchemaHash,
                reg.MigrationNewSchemaHash);

            var fileName = TypeNameSanitizer.ToShortIdentifier(migrationType) + "Step.g.cs";
            spc.AddSource(fileName, SourceText.From(text, Encoding.UTF8));
        }

        private static void EmitDiagnostics(
            SourceProductionContext spc,
            RegistrationRecord reg,
            HashSet<string> emitted)
        {
            if (reg?.Diagnostics == null) return;

            for (int i = 0; i < reg.Diagnostics.Count; i++)
            {
                var d = reg.Diagnostics[i];
                if (d == null) continue;

                var location = d.Location;
                var key = d.Id + "|" +
                    (location.SourceTree?.FilePath ?? "") + "|" +
                    location.SourceSpan.Start + "|" +
                    location.SourceSpan.Length;

                if (!emitted.Add(key)) continue;
                spc.ReportDiagnostic(d);
            }
        }

        private static void EmitBindings(
            SourceProductionContext spc,
            System.Collections.Immutable.ImmutableArray<ScopeDescriptor> descriptors)
        {
            foreach (var scope in descriptors)
            {
                if (scope == null) continue;
                if (scope.IsAbstract) continue;

                var shortId = TypeNameSanitizer.ToShortIdentifier(scope.FullTypeName);

                var bindingsText = BindingEmitter.Emit(scope);
                if (!string.IsNullOrEmpty(bindingsText))
                {
                    var fileName = shortId + "_ECSaveBindings.g.cs";
                    spc.AddSource(fileName, SourceText.From(bindingsText, Encoding.UTF8));
                }

                var regText = BindingEmitter.EmitRegistration(scope);
                if (!string.IsNullOrEmpty(regText))
                {
                    var regFileName = shortId + "_ECSaveRegistration.g.cs";
                    spc.AddSource(regFileName, SourceText.From(regText, Encoding.UTF8));
                }
            }
        }

        private static void EmitSelfChecks(
            SourceProductionContext spc,
            System.Collections.Immutable.ImmutableArray<ScopeDescriptor> descriptors)
        {
            var emitted = new HashSet<string>();

            foreach (var scope in descriptors)
            {
                if (scope == null) continue;

                foreach (var reg in scope.Registrations)
                {
                    if (string.IsNullOrEmpty(reg.TypeArgumentFullName)) continue;
                    if (!emitted.Add(reg.TypeArgumentFullName)) continue;

                    int expectedSize = reg.Plan != null ? reg.Plan.StructSize : 0;

                    var text = SelfCheckEmitter.Emit(reg.TypeArgumentFullName, expectedSize);
                    var fileName = TypeNameSanitizer.ToShortIdentifier(reg.TypeArgumentFullName)
                        + "_ECSaveSelfCheck.g.cs";
                    spc.AddSource(fileName, SourceText.From(text, Encoding.UTF8));
                }
            }
        }

        private static void EmitSelections(
    SourceProductionContext spc,
    System.Collections.Immutable.ImmutableArray<ScopeDescriptor> descriptors)
        {
            var emitted = new HashSet<string>();

            foreach (var scope in descriptors)
            {
                if (scope == null) continue;

                foreach (var reg in scope.Registrations)
                {
                    if (reg.Kind != RegistrationKind.SelectEntityReference
                        && reg.Kind != RegistrationKind.SelectEntityReferences)
                        continue;

                    if (string.IsNullOrEmpty(reg.TypeArgumentFullName)) continue;
                    if (string.IsNullOrEmpty(reg.FieldPath)) continue;

                    bool isBuffer = reg.Kind == RegistrationKind.SelectEntityReferences;

                    var key = reg.TypeArgumentFullName + "|" + reg.FieldPath + "|" + isBuffer;
                    if (!emitted.Add(key)) continue;

                    var text = SelectionModuleEmitter.Emit(
                        reg.TypeArgumentFullName,
                        reg.FieldPath,
                        isBuffer);

                    var fileName = TypeNameSanitizer.ToShortIdentifier(reg.TypeArgumentFullName)
                        + "_"
                        + TypeNameSanitizer.ToIdentifier(reg.FieldPath)
                        + "SelectionModule.g.cs";
                    spc.AddSource(fileName, SourceText.From(text, Encoding.UTF8));
                }
            }
        }
    }
}
