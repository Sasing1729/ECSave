using System.Collections.Generic;
using System.Linq;
using ECSave.SourceGenerator.Model;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Model
{
    public class MigrationConstraintAnalyzerTests
    {
        [Test]
        public void ValidMigration_NoDiagnostics()
        {
            var diags = Analyze(@"
using ECSave.Internal;

public sealed class MyMigration : IComponentMigration<int, long>
{
    public long Migrate(in int old) { return old; }
}");
            Assert.AreEqual(0, diags.Count,
                "diags: " + string.Join(", ", diags.Select(d => d.Id)));
        }

        [Test]
        public void NoDefaultCtor_ECSAVE031()
        {
            var diags = Analyze(@"
public sealed class NoCtor
{
    public NoCtor(int x) { }
}");
            Assert.IsTrue(diags.Any(d => d.Id == "ECSAVE031"));
        }

        private static List<Diagnostic> Analyze(string src)
        {
            var stub = StubSourceBuilder.BuilderStubs();
            var tree = CSharpSyntaxTree.ParseText(src);
            var stubTree = CSharpSyntaxTree.ParseText(stub);
            var compilation = CSharpCompilation.Create(
                "Test", new[] { tree, stubTree },
                ReferenceAssemblies.Get(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax cd)
                {
                    var sym = model.GetDeclaredSymbol(cd) as INamedTypeSymbol;
                    if (sym == null) continue;
                    if (sym.Name == "IComponentMigration") continue;

                    var diags = new List<Diagnostic>();
                    MigrationConstraintAnalyzer.Analyze(sym, cd.GetLocation(), diags);
                    return diags;
                }
            }
            return new List<Diagnostic>();
        }
    }
}
