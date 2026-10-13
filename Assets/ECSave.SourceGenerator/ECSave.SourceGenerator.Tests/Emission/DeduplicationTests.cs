using System.Linq;
using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class DeduplicationTests
    {
        [Test]
        public void SameTypeInMultipleScopes_ModuleEmittedOnce()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class ScopeA : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}

public sealed class ScopeB : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}";
            var result = GeneratorTestHarness.Run(src);

            int moduleCount = result.GeneratedTrees.Count(
                t => t.FilePath.EndsWith("HealthModule.g.cs"));
            Assert.AreEqual(1, moduleCount, "HealthModule should be emitted exactly once");
        }

        [Test]
        public void DifferentTypesInDifferentScopes_BothEmitted()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct A : IComponentData { public int V; }
public struct B : IComponentData { public int V; }

public sealed class ScopeA : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<A>(); }
}

public sealed class ScopeB : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<B>(); }
}";
            var result = GeneratorTestHarness.Run(src);

            Assert.IsTrue(result.GeneratedTrees.Any(t => t.FilePath.EndsWith("AModule.g.cs")));
            Assert.IsTrue(result.GeneratedTrees.Any(t => t.FilePath.EndsWith("BModule.g.cs")));
        }
    }
}
