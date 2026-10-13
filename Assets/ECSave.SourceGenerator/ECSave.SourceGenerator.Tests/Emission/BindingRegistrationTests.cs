using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class BindingRegistrationTests
    {
        [Test]
        public void Generator_EmitsRegistrationClass()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class GameScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "GameScope_ECSaveRegistration.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("RuntimeInitializeOnLoadMethod"));
            Assert.IsTrue(text.Contains("SubsystemRegistration"));
        }

        [Test]
        public void Registration_ContainsScopeName()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "S_ECSaveRegistration.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("\"S\""));
        }

        [Test]
        public void Registration_CallsGeneratedScopeRegistry()
        {
            var src = @"
using ECSave;

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "S_ECSaveRegistration.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("GeneratedScopeRegistry.Register"));
            Assert.IsTrue(text.Contains("new S_ECSaveBindings()"));
        }

        [Test]
        public void AbstractScope_NoRegistration()
        {
            var src = @"
using ECSave;

public abstract class AbstractScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "AbstractScope_ECSaveRegistration.g.cs");
            Assert.IsNull(text);
        }

        [Test]
        public void Registration_HasSubsystemRegistrationAttribute()
        {
            var src = @"
using ECSave;

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "S_ECSaveRegistration.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("RuntimeInitializeLoadType.SubsystemRegistration"));
        }
    }
}
