using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Discovery
{
    public class ConfigureCallCollectorTests
    {
        [Test]
        public void CountsNoRegistrations()
        {
            var src = @"
using ECSave;

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("RegistrationCounts"));
            Assert.IsTrue(text.Contains("           0,"));
        }

        [Test]
        public void CountsRegisterComponentCall()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int Value; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder)
    {
        builder.RegisterComponent<Health>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("           1,"));
        }

        [Test]
        public void CountsMultipleRegistrations()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct A : IComponentData { }
public struct B : IComponentData { }
public struct C : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder)
    {
        builder.RegisterComponent<A>();
        builder.RegisterComponent<B>();
        builder.RegisterComponent<C>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsTrue(text.Contains("           3,"));
        }

        [Test]
        public void CountsRegistrationsInsideIfBlock()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct A : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder)
    {
        if (true)
        {
            builder.RegisterComponent<A>();
        }
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsTrue(text.Contains("           1,"));
        }

        [Test]
        public void ExpressionBodyConfigure_Works()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct A : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder)
        => builder.RegisterComponent<A>();
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsTrue(text.Contains("           1,"));
        }
    }
}
