using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Model;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using System.Linq;

namespace ECSave.SourceGenerator.Tests.Model
{
    public class TypeAnalyzerTests
    {
        [Test]
        public void SimpleSequential_NoDiagnostics()
        {
            var result = Analyze(@"
public struct Health
{
    public int Current;
    public int Max;
}");

            Assert.AreEqual(0, result.Diagnostics.Count);
            Assert.IsNotNull(result.Plan);
            Assert.AreEqual(8, result.Plan.StructSize);
            Assert.AreEqual(2, result.Plan.Fields.Count);
        }

        [Test]
        public void Explicit_Layout()
        {
            var result = Analyze(@"
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
public struct Union
{
    [FieldOffset(0)] public int A;
    [FieldOffset(0)] public float B;
}");

            Assert.IsNotNull(result.Plan);
            Assert.AreEqual(2, result.Plan.Fields.Count);
        }

        [Test]
        public void Auto_WithEntity_Diagnostic()
        {
            var result = Analyze(@"
using System.Runtime.InteropServices;
using Unity.Entities;

[StructLayout(LayoutKind.Auto)]
public struct WithEntity
{
    public Entity E;
    public int V;
}");

            Assert.IsTrue(result.Diagnostics.Any(
                d => d.Id == "ECSAVE022"));
        }

        [Test]
        public void Auto_WithoutEntity_Diagnostic()
        {
            var result = Analyze(@"
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public struct Plain
{
    public int A;
    public int B;
}");

            Assert.IsTrue(result.Diagnostics.Any(
                d => d.Id == "ECSAVE029"));
        }

        [Test]
        public void PackAttribute_Diagnostic()
        {
            var result = Analyze(@"
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Packed
{
    public int A;
    public byte B;
}");

            Assert.IsTrue(result.Diagnostics.Any(
                d => d.Id == "ECSAVE025"));
        }

        [Test]
        public void PrivateField_Diagnostic()
        {
            var result = Analyze(@"
public struct WithPrivate
{
    private int Hidden;
    public int Visible;
}");

            Assert.IsTrue(result.Diagnostics.Any(
                d => d.Id == "ECSAVE023"));
        }

        [Test]
        public void EntityField_RefOffsetRecorded()
        {
            var result = Analyze(@"
using Unity.Entities;

public struct WithRef
{
    public Entity Target;
    public int Value;
}");

            Assert.IsNotNull(result.Plan);
            Assert.AreEqual(1, result.Plan.EntityRefOffsets.Length);
            Assert.AreEqual(0, result.Plan.EntityRefOffsets[0]);
            Assert.AreEqual(12, result.Plan.StructSize);
            Assert.AreEqual(8, result.Plan.RecordStride);

            var entityField = result.Plan.Fields[0];
            Assert.AreEqual(FieldKind.Entity, entityField.Kind);
            Assert.AreEqual(8, entityField.Size);

            // 追加：後続フィールドのオフセット
            var intField = result.Plan.Fields[1];
            Assert.AreEqual(8, intField.Offset);
            Assert.AreEqual(4, intField.Size);
        }

        private static TypeAnalysisResult Analyze(string source)
        {
            var stub = StubSourceBuilder.BuilderStubs();
            var tree = CSharpSyntaxTree.ParseText(source);
            var stubTree = CSharpSyntaxTree.ParseText(stub);
            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { tree, stubTree },
                ReferenceAssemblies.Get(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var symbol = FindFirstStruct(compilation, tree);
            return TypeAnalyzer.Analyze(symbol);
        }

        private static INamedTypeSymbol FindFirstStruct(Compilation compilation, SyntaxTree tree)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax sd)
                {
                    var sym = model.GetDeclaredSymbol(sd) as INamedTypeSymbol;
                    if (sym != null) return sym;
                }
            }
            return null;
        }
    }
}
