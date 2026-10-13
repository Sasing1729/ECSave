using System.Linq;
using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class FilterQueryEmissionTests
    {
        [Test]
        public void PlainComponent_NoFilter_NoBuildQueryOverride()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsFalse(text.Contains("BuildQuery"));
        }

        [Test]
        public void WithAll_EmitsBuildQueryOverride()
        {
            var src = @"
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
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("BuildQuery"));
            Assert.IsTrue(text.Contains("WithAll<global::Player>"));
        }

        [Test]
        public void WithAny_EmitsOrGroup()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }
public struct A : IComponentData { }
public struct B : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>().WithAny<A>().WithAny<B>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("WithAny<global::A>"));
            Assert.IsTrue(text.Contains("WithAny<global::B>"));
        }

        [Test]
        public void WithNone_EmitsExclusion()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }
public struct Dead : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>().WithNone<Dead>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("WithNone<global::Dead>"));
        }

        [Test]
        public void WithoutEnableableState_EmitsHasEnabledBitsFalse()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Stunned : IComponentData, IEnableableComponent { public int F; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Stunned>().WithoutEnableableState();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "StunnedModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsFalse(text.Contains("HasEnabledBits => true"));
        }
    }
}
