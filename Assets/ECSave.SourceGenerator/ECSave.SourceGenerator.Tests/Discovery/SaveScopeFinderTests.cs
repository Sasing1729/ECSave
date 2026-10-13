using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Discovery
{
    public class SaveScopeFinderTests
    {
        [Test]
        public void DetectsSaveScopeDerivedClass()
        {
            var src = @"
using ECSave;

public sealed class MyScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("MyScope"));
        }

        [Test]
        public void IgnoresNonSaveScopeClass()
        {
            var src = @"public sealed class NotAScope { }";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsNull(text);
        }

        [Test]
        public void IgnoresAbstractSaveScope()
        {
            var src = @"
using ECSave;

public abstract class AbstractScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("AbstractScope"));
        }

        [Test]
        public void DetectsMultipleScopes()
        {
            var src = @"
using ECSave;

public sealed class ScopeA : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder) { }
}

public sealed class ScopeB : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder builder) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsTrue(text.Contains("ScopeA"));
            Assert.IsTrue(text.Contains("ScopeB"));
        }
    }
}
