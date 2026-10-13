using System.Collections.Generic;
using System.Linq;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Discovery
{
    public class FluentChainTests
    {
        [Test]
        public void WithAll_RecordedOnce()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }
public struct Player : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>().WithAll<Player>();
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("Health", records[0].TypeArgumentFullName);
            Assert.AreEqual(1, records[0].Filter.WithAll.Count);
            Assert.AreEqual("Player", records[0].Filter.WithAll[0]);
        }

        [Test]
        public void ChainedFluentCalls_AllRecorded()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }
public struct Player : IComponentData { }
public struct Enemy : IComponentData { }
public struct Dead : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>()
            .WithAll<Player>()
            .WithNone<Dead>()
            .WithAny<Enemy>();
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(1, records[0].Filter.WithAll.Count);
            Assert.AreEqual(1, records[0].Filter.WithAny.Count);
            Assert.AreEqual(1, records[0].Filter.WithNone.Count);
            Assert.AreEqual("Player", records[0].Filter.WithAll[0]);
            Assert.AreEqual("Enemy", records[0].Filter.WithAny[0]);
            Assert.AreEqual("Dead", records[0].Filter.WithNone[0]);
        }

        [Test]
        public void WithoutEnableableState_Recorded()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Stunned : IComponentData, IEnableableComponent { public int F; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Stunned>().WithoutEnableableState();
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.IsTrue(records[0].WithoutEnableableState);
        }

        [Test]
        public void RenamedFrom_Recorded()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>().RenamedFrom(""Game.Domain.OldHealth"");
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("Game.Domain.OldHealth", records[0].RenamedFromOldName);
        }

        [Test]
        public void MultipleWithAll_AllRecorded()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }
public struct A : IComponentData { }
public struct B : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>().WithAll<A>().WithAll<B>();
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(2, records[0].Filter.WithAll.Count);
        }

        [Test]
        public void MultipleRegistrations_EachWithOwnFilter()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }
public struct Position : IComponentData { public int V; }
public struct Player : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>().WithAll<Player>();
        b.RegisterComponent<Position>();
    }
}");
            Assert.AreEqual(2, records.Count);
            Assert.AreEqual(1, records[0].Filter.WithAll.Count);
            Assert.AreEqual(0, records[1].Filter.WithAll.Count);
        }

        [Test]
        public void NoFluent_EmptyFilter()
        {
            var records = Collect(@"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
    }
}");
            Assert.AreEqual(1, records.Count);
            Assert.IsTrue(records[0].Filter.IsEmpty);
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
