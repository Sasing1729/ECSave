using ECSave.SourceGenerator.Model;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Model
{
    public class AlignmentTests
    {
        [Test]
        public void ByteThenEntity_PaddedToEntityBoundary()
        {
            var result = Analyze(@"
using Unity.Entities;

public struct S
{
    public byte A;
    public Entity B;
}");
            Assert.AreEqual(12, result.Plan.StructSize);
            Assert.AreEqual(0, result.Plan.Fields[0].Offset);
            Assert.AreEqual(4, result.Plan.Fields[1].Offset);
        }

        [Test]
        public void BoolThenLong_PaddedToLongBoundary()
        {
            var result = Analyze(@"
public struct S
{
    public bool A;
    public long B;
}");
            Assert.AreEqual(16, result.Plan.StructSize);
            Assert.AreEqual(0, result.Plan.Fields[0].Offset);
            Assert.AreEqual(8, result.Plan.Fields[1].Offset);
        }

        [Test]
        public void IntThenByte_TrailingPadding()
        {
            var result = Analyze(@"
public struct S
{
    public int A;
    public byte B;
}");
            Assert.AreEqual(8, result.Plan.StructSize);
            Assert.AreEqual(0, result.Plan.Fields[0].Offset);
            Assert.AreEqual(4, result.Plan.Fields[1].Offset);
        }

        [Test]
        public void EntityThenInt_NoExtraPadding()
        {
            var result = Analyze(@"
using Unity.Entities;

public struct S
{
    public Entity A;
    public int B;
}");
            Assert.AreEqual(12, result.Plan.StructSize);
            Assert.AreEqual(0, result.Plan.Fields[0].Offset);
            Assert.AreEqual(8, result.Plan.Fields[1].Offset);
        }

        [Test]
        public void AllPrimitives_AlignedTotal()
        {
            var result = Analyze(@"
public struct S
{
    public bool B;
    public sbyte I8;
    public short I16;
    public int I32;
    public long I64;
    public byte U8;
    public ushort U16;
    public uint U32;
    public ulong U64;
    public float F32;
    public double F64;
}");
            Assert.AreEqual(48, result.Plan.StructSize);
        }

        [Test]
        public void NestedStruct_RespectsInnerAlignment()
        {
            var result = AnalyzeOfType(@"
public struct Inner { public long L; public int I; }
public struct Outer { public byte B; public Inner N; }", "Outer");
            Assert.AreEqual(0, result.Plan.Fields[0].Offset);
            Assert.AreEqual(8, result.Plan.Fields[1].Offset);
            Assert.AreEqual(16, result.Plan.Fields[1].Size);
            Assert.AreEqual(24, result.Plan.StructSize);
        }

        private static TypeAnalysisResult Analyze(string src) => AnalyzeOfType(src, null);

        private static TypeAnalysisResult AnalyzeOfType(string src, string typeName)
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
                var sd = node as StructDeclarationSyntax;
                if (sd == null) continue;
                if (typeName != null && sd.Identifier.Text != typeName) continue;

                var sym = model.GetDeclaredSymbol(sd) as INamedTypeSymbol;
                if (sym != null) return TypeAnalyzer.Analyze(sym);
            }
            return null;
        }
    }
}
