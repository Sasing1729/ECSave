using System;
using System.Collections.Generic;
using ECSave.SourceGenerator.Analyzers;
using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Emission;
using ECSave.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ECSave.SourceGenerator.Discovery
{
    internal static class ConfigureCallCollector
    {
        public const int MaxDepth = 8;

        public static IReadOnlyList<RegistrationRecord> Collect(
            IMethodSymbol configureMethod,
            Compilation compilation)
        {
            var records = new List<RegistrationRecord>();
            if (configureMethod == null || compilation == null) return records;

            IReadOnlyList<SyntaxReference> syntaxRefs;
            try
            {
                syntaxRefs = configureMethod.DeclaringSyntaxReferences;
            }
            catch
            {
                return records;
            }

            foreach (var syntaxRef in syntaxRefs)
            {
                try
                {
                    var node = syntaxRef.GetSyntax();
                    var methodDecl = node as MethodDeclarationSyntax;
                    if (methodDecl == null) continue;

                    SemanticModel model;
                    try
                    {
                        model = compilation.GetSemanticModel(methodDecl.SyntaxTree);
                    }
                    catch
                    {
                        continue;
                    }

                    if (methodDecl.Body != null)
                        Walk(methodDecl.Body, model, records, 0);

                    if (methodDecl.ExpressionBody != null)
                        Walk(methodDecl.ExpressionBody.Expression, model, records, 0);
                }
                catch
                {
                    // 環境依存の失敗は該当 syntaxRef のみスキップ
                }
            }

            return records;
        }

        private static void Walk(
            SyntaxNode node,
            SemanticModel model,
            List<RegistrationRecord> records,
            int depth)
        {
            if (node == null) return;

            var invocation = node as InvocationExpressionSyntax;
            if (invocation != null && !IsInnerOfFluentChain(invocation))
            {
                try
                {
                    AnalyzeChain(invocation, model, records);
                }
                catch
                {
                    // 該当呼び出しの解析失敗は他に影響させない
                }
            }

            int nextDepth = depth;
            if (IsDepthContainer(node))
            {
                if (depth >= MaxDepth) return;
                nextDepth = depth + 1;
            }

            foreach (var child in node.ChildNodes())
            {
                try
                {
                    Walk(child, model, records, nextDepth);
                }
                catch
                {
                }
            }
        }

        private static bool IsInnerOfFluentChain(InvocationExpressionSyntax invocation)
        {
            var parent = invocation.Parent as MemberAccessExpressionSyntax;
            if (parent == null) return false;
            return parent.Expression == invocation;
        }

        private static bool IsDepthContainer(SyntaxNode node)
        {
            return node is IfStatementSyntax
                || node is ForStatementSyntax
                || node is ForEachStatementSyntax
                || node is WhileStatementSyntax
                || node is DoStatementSyntax
                || node is SwitchStatementSyntax
                || node is TryStatementSyntax;
        }

        private static void AnalyzeChain(
            InvocationExpressionSyntax outer,
            SemanticModel model,
            List<RegistrationRecord> records)
        {
            var invocations = new List<InvocationExpressionSyntax>();
            SyntaxNode node = outer;
            while (node is InvocationExpressionSyntax inv)
            {
                invocations.Add(inv);
                var ma = inv.Expression as MemberAccessExpressionSyntax;
                if (ma == null) break;
                node = ma.Expression;
            }

            if (invocations.Count == 0) return;
            invocations.Reverse();

            var firstInvocation = invocations[0];
            var firstMethod = GetMethodSymbol(firstInvocation, model);
            if (firstMethod == null) return;

            string firstMethodName;
            try { firstMethodName = firstMethod.Name; }
            catch { return; }

            var kind = WellKnownTypes.MapMethodName(firstMethodName);
            if (kind == RegistrationKind.Unknown) return;

            INamedTypeSymbol containingType;
            try { containingType = firstMethod.ContainingType; }
            catch { return; }

            if (containingType == null || !WellKnownTypes.IsBuilderType(containingType))
                return;

            var withAll = new List<string>();
            var withAny = new List<string>();
            var withNone = new List<string>();
            bool withoutEnableable = false;
            string renamedFrom = null;

            for (int i = 1; i < invocations.Count; i++)
            {
                var inv = invocations[i];
                IMethodSymbol m;
                try { m = GetMethodSymbol(inv, model); }
                catch { continue; }

                if (m == null) continue;

                string methodName;
                try { methodName = m.Name; }
                catch { continue; }

                string typeArg = null;
                try
                {
                    if (m.TypeArguments.Length > 0)
                        typeArg = m.TypeArguments[0].ToDisplayString();
                }
                catch
                {
                    typeArg = null;
                }

                switch (methodName)
                {
                    case "WithAll":
                        if (typeArg != null) withAll.Add(typeArg);
                        break;
                    case "WithAny":
                        if (typeArg != null) withAny.Add(typeArg);
                        break;
                    case "WithNone":
                        if (typeArg != null) withNone.Add(typeArg);
                        break;
                    case "WithoutEnableableState":
                        withoutEnableable = true;
                        break;
                    case "RenamedFrom":
                        renamedFrom = ExtractStringArgument(inv);
                        break;
                }
            }

            var filter = new FilterInfo(withAll, withAny, withNone);
            var diagnostics = new List<Diagnostic>();

            string containingTypeName;
            try { containingTypeName = containingType.ToDisplayString(); }
            catch { containingTypeName = string.Empty; }

            if (kind == RegistrationKind.SelectEntityReference
                || kind == RegistrationKind.SelectEntityReferences)
            {
                try
                {
                    SelectFieldAnalyzer.Analyze(firstInvocation, model, diagnostics);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.AnalysisFailed,
                        SafeLocation(firstInvocation),
                        containingTypeName,
                        "Select analysis: " + ex.GetType().Name));
                }

                string fieldPath = null;
                try
                {
                    fieldPath = SelectFieldAnalyzer.ExtractFieldPath(firstInvocation);
                }
                catch
                {
                    fieldPath = null;
                }

                string typeArgName = GetFirstTypeArgumentName(firstMethod);

                records.Add(new RegistrationRecord(
                    kind,
                    containingTypeName,
                    typeArgName,
                    records.Count,
                    ModuleClass.Unknown,
                    null,
                    null,
                    0,
                    diagnostics,
                    fieldPath,
                    filter));
                return;
            }

            if (kind == RegistrationKind.Identity)
            {
                string typeArgName = GetFirstTypeArgumentName(firstMethod);

                TypePlan idPlan = null;
                byte[] idInput = null;
                ulong idHash = 0;

                if (firstMethod.TypeArguments.Length > 0)
                {
                    try
                    {
                        var idType = firstMethod.TypeArguments[0];
                        IdentityConstraintAnalyzer.Analyze(
                            idType,
                            SafeLocation(firstInvocation),
                            diagnostics);
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(Diagnostic.Create(
                            DiagnosticDescriptors.AnalysisFailed,
                            SafeLocation(firstInvocation),
                            typeArgName,
                            "Identity analysis: " + ex.GetType().Name));
                    }

                    try
                    {
                        var idTypeSym = firstMethod.TypeArguments[0] as INamedTypeSymbol;
                        var idAnalysis = TypeAnalyzer.Analyze(idTypeSym);
                        idPlan = idAnalysis.Plan;
                        if (idAnalysis.Diagnostics != null)
                        {
                            for (int i = 0; i < idAnalysis.Diagnostics.Count; i++)
                                if (idAnalysis.Diagnostics[i] != null)
                                    diagnostics.Add(idAnalysis.Diagnostics[i]);
                        }
                        if (idPlan != null)
                        {
                            idInput = SchemaHashInputBuilder.Build(idPlan);
                            idHash = SchemaHashCore.Hash(idInput);
                        }
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(Diagnostic.Create(
                            DiagnosticDescriptors.AnalysisFailed,
                            SafeLocation(firstInvocation),
                            typeArgName,
                            "Identity type analysis: " + ex.GetType().Name));
                        idPlan = null;
                        idInput = null;
                        idHash = 0;
                    }
                }

                records.Add(new RegistrationRecord(
                    kind,
                    containingTypeName,
                    typeArgName,
                    records.Count,
                    ModuleClass.Identity,
                    idPlan,
                    idInput,
                    idHash,
                    diagnostics,
                    null,
                    filter));
                return;
            }

            if (kind == RegistrationKind.Migration)
            {
                string typeArgName = GetFirstTypeArgumentName(firstMethod);

                string oldTypeName = null;
                string newTypeName = null;
                TypePlan oldPlan = null;
                TypePlan newPlan = null;
                ulong oldHash = 0UL;
                ulong newHash = 0UL;
                ulong newTypeNameHash = 0UL;

                if (firstMethod.TypeArguments.Length > 0)
                {
                    INamedTypeSymbol migType = null;
                    try
                    {
                        migType = firstMethod.TypeArguments[0] as INamedTypeSymbol;
                    }
                    catch
                    {
                        migType = null;
                    }

                    if (migType != null)
                    {
                        try
                        {
                            MigrationConstraintAnalyzer.Analyze(
                                migType,
                                SafeLocation(firstInvocation),
                                diagnostics);
                        }
                        catch (Exception ex)
                        {
                            diagnostics.Add(Diagnostic.Create(
                                DiagnosticDescriptors.AnalysisFailed,
                                SafeLocation(firstInvocation),
                                typeArgName,
                                "Migration constraint analysis: " + ex.GetType().Name));
                        }

                        try
                        {
                            if (MigrationConstraintAnalyzer.TryExtractTargetTypes(
                                migType, out var oldSym, out var newSym))
                            {
                                oldTypeName = oldSym.ToDisplayString();
                                newTypeName = newSym.ToDisplayString();

                                var oldResult = TypeAnalyzer.Analyze(oldSym as INamedTypeSymbol);
                                oldPlan = oldResult.Plan;
                                if (oldPlan != null)
                                    oldHash = SchemaHashCore.Hash(
                                        SchemaHashInputBuilder.Build(oldPlan));

                                var newResult = TypeAnalyzer.Analyze(newSym as INamedTypeSymbol);
                                newPlan = newResult.Plan;
                                if (newPlan != null)
                                    newHash = SchemaHashCore.Hash(
                                        SchemaHashInputBuilder.Build(newPlan));

                                newTypeNameHash = SchemaHashCore.HashUtf8(newTypeName);
                            }
                        }
                        catch (Exception ex)
                        {
                            diagnostics.Add(Diagnostic.Create(
                                DiagnosticDescriptors.AnalysisFailed,
                                SafeLocation(firstInvocation),
                                typeArgName,
                                "Migration type analysis: " + ex.GetType().Name));
                            oldPlan = null;
                            newPlan = null;
                            oldHash = 0UL;
                            newHash = 0UL;
                            newTypeNameHash = 0UL;
                        }
                    }
                }

                records.Add(new RegistrationRecord(
                    kind,
                    containingTypeName,
                    typeArgName,
                    records.Count,
                    ModuleClass.Unknown,
                    null,
                    null,
                    0,
                    diagnostics,
                    null,
                    filter,
                    false,
                    null,
                    oldTypeName,
                    newTypeName,
                    oldPlan,
                    newPlan,
                    oldHash,
                    newHash,
                    newTypeNameHash));
                return;
            }

            if (firstMethod.TypeArguments.Length == 0)
            {
                records.Add(new RegistrationRecord(
                    kind,
                    containingTypeName,
                    string.Empty,
                    records.Count,
                    ModuleClass.Unknown,
                    null,
                    null,
                    0,
                    null,
                    null,
                    filter,
                    withoutEnableable,
                    renamedFrom));
                return;
            }

            ITypeSymbol typeArgSym;
            try { typeArgSym = firstMethod.TypeArguments[0]; }
            catch { return; }

            string typeName;
            try { typeName = typeArgSym.ToDisplayString(); }
            catch { typeName = string.Empty; }

            ModuleClass classified;
            try { classified = TypeModuleClassifier.Classify(typeArgSym, kind); }
            catch
            {
                classified = ModuleClass.Unknown;
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.AnalysisFailed,
                    SafeLocation(firstInvocation),
                    typeName,
                    "Classification failed"));
            }

            TypePlan plan = null;
            byte[] input = null;
            ulong hash = 0;

            if (classified != ModuleClass.Unknown)
            {
                try
                {
                    var analysis = TypeAnalyzer.Analyze(typeArgSym as INamedTypeSymbol);
                    plan = analysis.Plan;
                    if (analysis.Diagnostics != null)
                    {
                        for (int i = 0; i < analysis.Diagnostics.Count; i++)
                            if (analysis.Diagnostics[i] != null)
                                diagnostics.Add(analysis.Diagnostics[i]);
                    }
                    if (plan != null)
                    {
                        input = SchemaHashInputBuilder.Build(plan);
                        hash = SchemaHashCore.Hash(input);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.AnalysisFailed,
                        SafeLocation(firstInvocation),
                        typeName,
                        "Type analysis: " + ex.GetType().Name));
                    plan = null;
                    input = null;
                    hash = 0;
                }
            }

            records.Add(new RegistrationRecord(
                kind,
                containingTypeName,
                typeName,
                records.Count,
                classified,
                plan,
                input,
                hash,
                diagnostics,
                null,
                filter,
                withoutEnableable,
                renamedFrom));
        }

        private static string GetFirstTypeArgumentName(IMethodSymbol method)
        {
            try
            {
                if (method.TypeArguments.Length > 0)
                    return method.TypeArguments[0].ToDisplayString();
            }
            catch
            {
            }
            return string.Empty;
        }

        private static IMethodSymbol GetMethodSymbol(
            InvocationExpressionSyntax invocation,
            SemanticModel model)
        {
            SymbolInfo symbolInfo;
            try
            {
                symbolInfo = model.GetSymbolInfo(invocation);
            }
            catch
            {
                return null;
            }

            var method = symbolInfo.Symbol as IMethodSymbol;

            if (method == null && symbolInfo.CandidateSymbols.Length > 0)
            {
                for (int i = 0; i < symbolInfo.CandidateSymbols.Length; i++)
                {
                    var cand = symbolInfo.CandidateSymbols[i] as IMethodSymbol;
                    if (cand != null) { method = cand; break; }
                }
            }

            return method;
        }

        private static string ExtractStringArgument(InvocationExpressionSyntax invocation)
        {
            var args = invocation.ArgumentList?.Arguments;
            if (args == null || args.Value.Count < 1) return null;

            var lit = args.Value[0].Expression as LiteralExpressionSyntax;
            if (lit == null) return null;

            return lit.Token.ValueText;
        }

        private static Location SafeLocation(SyntaxNode node)
        {
            try
            {
                return node?.GetLocation() ?? Location.None;
            }
            catch
            {
                return Location.None;
            }
        }
    }
}
