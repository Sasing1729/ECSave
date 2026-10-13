using System.Linq;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Discovery
{
    public class ConfigureCallCollectorExtendedTests
    {
        [Test]
        public void DetectsAllRegistrationKinds()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct C : IComponentData { }
public struct B : IBufferElementData { }
public struct S : ISharedComponentData { }
public struct Ch : IComponentData { }
public struct Id : IComponentData { }

public sealed class Scope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<C>();
        b.RegisterBuffer<B>();
        b.RegisterSharedComponent<S>();
        b.RegisterChunkComponent<Ch>();
        b.RegisterIdentity<Id>();
    }
}";
            var records = Collect(src);
            Assert.AreEqual(5, records.Count);
            Assert.IsTrue(records.Any(r => r.Kind == RegistrationKind.Component));
            Assert.IsTrue(records.Any(r => r.Kind == RegistrationKind.Buffer));
            Assert.IsTrue(records.Any(r => r.Kind == RegistrationKind.SharedComponent));
            Assert.IsTrue(records.Any(r => r.Kind == RegistrationKind.ChunkComponent));
            Assert.IsTrue(records.Any(r => r.Kind == RegistrationKind.Identity));
        }

        [Test]
        public void IgnoreNonBuilderCalls()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct C : IComponentData { }

public sealed class Scope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        System.Console.WriteLine(""test"");
        b.RegisterComponent<C>();
    }
}";
            var records = Collect(src);
            Assert.AreEqual(1, records.Count);
        }

        [Test]
        public void DeeplyNestedRegistrations_UpToMaxDepth()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct A : IComponentData { }

public sealed class Scope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        if (true) { if (true) { if (true) { if (true) {
            if (true) { if (true) { if (true) {
                b.RegisterComponent<A>();
            }}}}}}
        }
    }
}";
            var records = Collect(src);
            Assert.AreEqual(1, records.Count);
        }

        [Test]
        public void OrderReflectsEncounterOrder()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct A : IComponentData { }
public struct B : IComponentData { }
public struct C : IComponentData { }

public sealed class Scope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<A>();
        b.RegisterComponent<B>();
        b.RegisterComponent<C>();
    }
}";
            var records = Collect(src);
            Assert.AreEqual("A", records[0].TypeArgumentFullName);
            Assert.AreEqual("B", records[1].TypeArgumentFullName);
            Assert.AreEqual("C", records[2].TypeArgumentFullName);
            Assert.AreEqual(0, records[0].Order);
            Assert.AreEqual(1, records[1].Order);
            Assert.AreEqual(2, records[2].Order);
        }

        [Test]
        public void ClassifiedAsPlain_ForComponent()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class Scope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
    }
}";
            var records = Collect(src);
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(ECSave.SourceGenerator.Emission.ModuleClass.Plain, records[0].ClassifiedAs);
            Assert.IsNotNull(records[0].Plan);
            Assert.IsTrue(records[0].SchemaHash != 0);
        }

        private static System.Collections.Generic.IReadOnlyList<RegistrationRecord> Collect(string src)
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
