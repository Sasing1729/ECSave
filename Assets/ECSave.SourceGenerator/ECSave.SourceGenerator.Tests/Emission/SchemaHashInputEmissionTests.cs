using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class SchemaHashInputEmissionTests
    {
        [Test]
        public void ZeroSizeType_EmitsTwoByteInput()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Tag : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Tag>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "TagModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("s_schemaHashInput = new byte[]"));
            Assert.IsTrue(text.Contains("0x00, 0x00,"));
        }

        [Test]
        public void WithFields_EmitsExpandedInput()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Two : IComponentData { public int A; public int B; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Two>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "TwoModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("s_schemaHashInput"));
            Assert.IsTrue(text.Contains("0x08, 0x00"));
        }
    }
}
