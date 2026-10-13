using System.Collections.Generic;
using ECSave.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ECSave.SourceGenerator.Model
{
    internal static class SelectFieldAnalyzer
    {
        public static bool Analyze(
            InvocationExpressionSyntax invocation,
            SemanticModel model,
            List<Diagnostic> diagnostics)
        {
            if (invocation == null || model == null || diagnostics == null)
                return false;

            var args = invocation.ArgumentList?.Arguments;
            if (args == null || args.Value.Count != 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SelectFieldFormat,
                    invocation.GetLocation()));
                return false;
            }

            var argExpr = args.Value[0].Expression;
            var lambda = argExpr as LambdaExpressionSyntax;

            if (lambda == null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SelectFieldFormat,
                    argExpr.GetLocation()));
                return false;
            }

            var body = lambda.Body;
            if (body == null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SelectFieldFormat,
                    lambda.GetLocation()));
                return false;
            }

            if (body is BlockSyntax)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SelectFieldFormat,
                    body.GetLocation()));
                return false;
            }

            return AnalyzeBody(body, model, diagnostics);
        }

        public static string ExtractFieldPath(InvocationExpressionSyntax invocation)
        {
            var args = invocation?.ArgumentList?.Arguments;
            if (args == null || args.Value.Count != 1) return null;

            var argExpr = args.Value[0].Expression;
            var lambda = argExpr as LambdaExpressionSyntax;
            if (lambda == null) return null;

            return ExtractFieldPathFromBody(lambda.Body);
        }

        private static bool AnalyzeBody(
            SyntaxNode body,
            SemanticModel model,
            List<Diagnostic> diagnostics)
        {
            var ma = body as MemberAccessExpressionSyntax;
            if (ma == null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SelectFieldFormat,
                    body.GetLocation()));
                return false;
            }

            var fieldName = ma.Name.Identifier.Text;

            var field = ResolveField(ma, model);

            if (field == null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SelectFieldMissing,
                    ma.GetLocation(),
                    fieldName));
                return false;
            }

            if (field.Type.ToDisplayString() != Discovery.WellKnownTypes.EntityMetadataName)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SelectFieldNotEntity,
                    ma.GetLocation(),
                    field.Name));
                return false;
            }

            return true;
        }

        private static IFieldSymbol ResolveField(
            MemberAccessExpressionSyntax ma,
            SemanticModel model)
        {
            var field = model.GetSymbolInfo(ma).Symbol as IFieldSymbol;
            if (field != null) return field;

            var receiverType = model.GetTypeInfo(ma.Expression).Type as INamedTypeSymbol;
            if (receiverType == null) return null;

            var name = ma.Name.Identifier.Text;
            foreach (var member in receiverType.GetMembers(name))
            {
                if (member is IFieldSymbol fs) return fs;
            }

            return null;
        }

        private static string ExtractFieldPathFromBody(SyntaxNode body)
        {
            var ma = body as MemberAccessExpressionSyntax;
            if (ma == null) return null;

            var parts = new List<string>();
            var current = ma;
            while (current != null)
            {
                parts.Insert(0, current.Name.Identifier.Text);
                var inner = current.Expression as MemberAccessExpressionSyntax;
                if (inner == null) break;
                current = inner;
            }
            return string.Join(".", parts);
        }
    }
}
