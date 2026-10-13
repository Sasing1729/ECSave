using System.Linq;
using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Model;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Model
{
    public class TypeAnalyzerExtendedTests
    {
        [Test]
        public void EnumField_MappedToUnderlyingInt32()
        {
            var result = Analyze(@"
public enum Color : int { Red = 0, Green = 1 }

public struct WithEnum
{
    public Color C;
    public int V;
}");
            Assert.AreEqual(0, result.Diagnostics.Count);
            Assert.AreEqual(2, result.Plan.Fields.Count);
            Assert.AreEqual(SchemaHashKind.Int32, result.Plan.Fields[0].ElementKind);
        }

        [Test]
        public void EnumByte_UnderlyingUInt8()
        {
            var result = Analyze(@"
public enum Small : byte { A = 0 }

public struct S
{
    public Small Value;
}");
            Assert.AreEqual(SchemaHashKind.UInt8, result.Plan.Fields[0].ElementKind);
            Assert.AreEqual(1, result.Plan.Fields[0].Size);
        }

        [Test]
        public void MultipleEntityFields_AllRecorded()
        {
            var result = Analyze(@"
using Unity.Entities;

public struct MultiRef
{
    public Entity A;
    public int X;
    public Entity B;
}");
            Assert.AreEqual(2, result.Plan.EntityRefOffsets.Length);
            Assert.AreEqual(0, result.Plan.EntityRefOffsets[0]);
            Assert.AreEqual(12, result.Plan.EntityRefOffsets[1]);
            Assert.AreEqual(20, result.Plan.StructSize);
            Assert.AreEqual(12, result.Plan.RecordStride);
        }

        [Test]
        public void NestedStruct_RecognizedAsNested()
        {
            var result = AnalyzeOfType(@"
public struct Inner { public int A; public int B; }

public struct Outer
{
    public Inner Nested;
    public int Tail;
}", "Outer");
            Assert.IsNotNull(result.Plan);
            Assert.AreEqual(2, result.Plan.Fields.Count);
            Assert.AreEqual(FieldKind.Nested, result.Plan.Fields[0].Kind);
            Assert.AreEqual(8, result.Plan.Fields[0].Size);
            Assert.AreEqual(0, result.Plan.Fields[0].Offset);
            Assert.AreEqual(FieldKind.Primitive, result.Plan.Fields[1].Kind);
            Assert.AreEqual(4, result.Plan.Fields[1].Size);
            Assert.AreEqual(8, result.Plan.Fields[1].Offset);
        }

        [Test]
        public void AllPrimitives_Mapped()
        {
            var result = Analyze(@"
public struct Prims
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
            Assert.AreEqual(0, result.Diagnostics.Count);
            Assert.AreEqual(11, result.Plan.Fields.Count);
            Assert.AreEqual(SchemaHashKind.Bool, result.Plan.Fields[0].ElementKind);
            Assert.AreEqual(SchemaHashKind.Float64, result.Plan.Fields[10].ElementKind);
        }

        [Test]
        public void StaticField_Ignored()
        {
            var result = Analyze(@"
public struct WithStatic
{
    public static int Shared;
    public int Instance;
}");
            Assert.AreEqual(1, result.Plan.Fields.Count);
            Assert.AreEqual("Instance", result.Plan.Fields[0].Name);
        }

        [Test]
        public void ExplicitLayout_TwoOverlappingFields()
        {
            var result = Analyze(@"
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
public struct Union
{
    [FieldOffset(0)] public int A;
    [FieldOffset(0)] public float B;
}");
            Assert.AreEqual(2, result.Plan.Fields.Count);
        }

        [Test]
        public void EntityFieldOnly_RecordStrideZero()
        {
            var result = Analyze(@"
using Unity.Entities;

public struct RefOnly
{
    public Entity Target;
}");
            Assert.AreEqual(1, result.Plan.EntityRefOffsets.Length);
            Assert.AreEqual(8, result.Plan.StructSize);
            Assert.AreEqual(4, result.Plan.RecordStride);
        }

        private static TypeAnalysisResult Analyze(string source)
            => AnalyzeOfType(source, null);

        private static TypeAnalysisResult AnalyzeOfType(string source, string typeName)
        {
            var stub = StubSourceBuilder.BuilderStubs();
            var tree = CSharpSyntaxTree.ParseText(source);
            var stubTree = CSharpSyntaxTree.ParseText(stub);
            var compilation = CSharpCompilation.Create(
                "Test",
                new[] { tree, stubTree },
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
