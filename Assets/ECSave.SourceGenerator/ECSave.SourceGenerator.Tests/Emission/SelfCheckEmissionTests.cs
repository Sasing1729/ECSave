using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class SelfCheckEmissionTests
    {
        [Test]
        public void SelfCheck_HasCorrectExpectedSize()
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
            var text = GeneratorTestHarness.GetGenerated(result, "Health_ECSaveSelfCheck.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("ExpectedSize = 8"));
        }

        [Test]
        public void SelfCheck_ZeroSizeTag_ExpectedZero()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Tag : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Tag>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "Tag_ECSaveSelfCheck.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("ExpectedSize = 0"));
        }
    }
}
