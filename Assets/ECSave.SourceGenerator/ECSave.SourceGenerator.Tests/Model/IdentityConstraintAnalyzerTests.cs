using System.Collections.Generic;
using System.Linq;
using ECSave.SourceGenerator.Model;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Model
{
    public class IdentityConstraintAnalyzerTests
    {
        [Test]
        public void ValidIdentityType_NoDiagnostics()
        {
            Assert.AreEqual(0, Analyze(@"
public struct PlayerId : Unity.Entities.IComponentData { public int Value; }").Count);
        }

        [Test]
        public void NotComponent_ECSAVE010()
        {
            var diags = Analyze(@"public struct Bad { public int V; }");
            Assert.IsTrue(diags.Any(d => d.Id == "ECSAVE010"));
        }

        [Test]
        public void BufferElement_ECSAVE011()
        {
            var diags = Analyze(@"public struct Bad : Unity.Entities.IBufferElementData { public int V; }");
            Assert.IsTrue(diags.Any(d => d.Id == "ECSAVE011"));
        }

        [Test]
        public void SharedComponent_ECSAVE012()
        {
            var diags = Analyze(@"public struct Bad : Unity.Entities.ISharedComponentData { public int V; }");
            Assert.IsTrue(diags.Any(d => d.Id == "ECSAVE012"));
        }

        [Test]
        public void EntityField_ECSAVE017()
        {
            var diags = Analyze(@"
public struct Bad : Unity.Entities.IComponentData { public Unity.Entities.Entity E; }");
            Assert.IsTrue(diags.Any(d => d.Id == "ECSAVE017"));
        }

        [Test]
        public void ExplicitLayout_ECSAVE014()
        {
            var diags = Analyze(@"
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
public struct Bad : Unity.Entities.IComponentData
{
    [FieldOffset(0)] public int A;
}");
            Assert.IsTrue(diags.Any(d => d.Id == "ECSAVE014"));
        }

        [Test]
        public void TooManyFields_ECSAVE015()
        {
            var fields = string.Join("\n", Enumerable.Range(0, 33).Select(i => $"public int F{i};"));
            var diags = Analyze($@"
public struct Bad : Unity.Entities.IComponentData
{{
    {fields}
}}");
            Assert.IsTrue(diags.Any(d => d.Id == "ECSAVE015"));
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
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax sd)
                {
                    var sym = model.GetDeclaredSymbol(sd) as INamedTypeSymbol;
                    if (sym == null) continue;
                    var diags = new List<Diagnostic>();
                    IdentityConstraintAnalyzer.Analyze(sym, sd.GetLocation(), diags);
                    return diags;
                }
            }
            return new List<Diagnostic>();
        }
    }
}
