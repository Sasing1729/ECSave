using System.Collections.Generic;
using System.Linq;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Discovery
{
    public class SelectRegistrationTests
    {
        [Test]
        public void SelectEntityReference_FieldPathRecorded()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => r.Target);
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(RegistrationKind.SelectEntityReference, records[0].Kind);
            Assert.AreEqual("Ref", records[0].TypeArgumentFullName);
            Assert.AreEqual("Target", records[0].FieldPath);
        }

        [Test]
        public void SelectEntityReferences_Buffer_FieldPathRecorded()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct RefElem : IBufferElementData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReferences<RefElem>(e => e.Target);
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(RegistrationKind.SelectEntityReferences, records[0].Kind);
            Assert.AreEqual("RefElem", records[0].TypeArgumentFullName);
            Assert.AreEqual("Target", records[0].FieldPath);
        }

        [Test]
        public void RegisterMigration_RecordedWithTypeArgument()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public sealed class MyMigration : System.IDisposable { public void Dispose() { } }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterMigration<MyMigration>();
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(RegistrationKind.Migration, records[0].Kind);
            Assert.AreEqual("MyMigration", records[0].TypeArgumentFullName);
        }

        private static IReadOnlyList<RegistrationRecord> Collect(string src)
        {
            var stub = StubSourceBuilder.BuilderStubs();
            var tree = CSharpSyntaxTree.ParseText(src);
            var stubTree = CSharpSyntaxTree.ParseText(stub);
            var compilation = CSharpCompilation.Create(
                "Test",
                new[] { tree, stubTree },
                ReferenceAssemblies.Get(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax cd)
                {
                    var sym = model.GetDeclaredSymbol(cd) as INamedTypeSymbol;
                    if (sym == null) continue;
                    if (!WellKnownTypes.IsSaveScope(sym)) continue;

                    foreach (var member in sym.GetMembers("Configure"))
                    {
                        var m = member as IMethodSymbol;
                        if (m != null && m.Parameters.Length == 1)
                            return ConfigureCallCollector.Collect(m, compilation);
                    }
                }
            }
            return new RegistrationRecord[0];
        }
    }
}
