using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class EndToEndEmitterTests
    {
        [Test]
        public void Generator_EmitsPlainModule()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int Current; public int Max; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModulePlain<global::Health>"));
        }

        [Test]
        public void Generator_EmitsEnableableModule()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Stunned : IComponentData, IEnableableComponent { public int Frames; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Stunned>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "StunnedModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleEnableable<global::Stunned>"));
        }

        [Test]
        public void Generator_EmitsBufferModule()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Item : IBufferElementData { public int Id; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterBuffer<Item>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ItemModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleBuffer<global::Item>"));
        }

        [Test]
        public void Generator_EmitsSharedModule()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Faction : ISharedComponentData { public int Id; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterSharedComponent<Faction>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "FactionModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleShared<global::Faction>"));
        }

        [Test]
        public void Generator_EmitsIdentityModule()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct PlayerId : IComponentData { public int Value; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterIdentity<PlayerId>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "PlayerIdIdentityModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("ECSave.Internal.IdentityModule"));
        }

        [Test]
        public void Generator_EmitsBinding()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class GameScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "GameScope_ECSaveBindings.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("ScopeId = \"GameScope\""));
            Assert.IsTrue(text.Contains("RegisterModule(new global::ECSave.Generated.Modules.HealthModule())"));
        }
    }
}
