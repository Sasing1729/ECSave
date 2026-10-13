using System.Linq;
using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Model
{
    public class SelectFieldAnalyzerTests
    {
        [Test]
        public void ValidSimpleField_NoDiagnostic()
        {
            var result = Run(@"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => r.Target);
    }
}");
            Assert.That(result.Diagnostics.Count, Is.EqualTo(0),
                "diagnostics: " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        [Test]
        public void NonMemberAccess_EmitsECSAVE001()
        {
            var result = Run(@"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => default(Entity));
    }
}");
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ECSAVE001"),
                "diagnostics: " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        [Test]
        public void MissingField_EmitsECSAVE003()
        {
            var result = Run(@"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => r.Missing);
    }
}");
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ECSAVE003"),
                "diagnostics: " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        [Test]
        public void NestedMemberAccess_NoDiagnostic()
        {
            var result = Run(@"
using ECSave;
using Unity.Entities;

public struct Inner { public Entity Target; }
public struct Ref : IComponentData { public Inner Nested; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => r.Nested.Target);
    }
}");
            Assert.That(result.Diagnostics.Count, Is.EqualTo(0),
                "diagnostics: " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        [Test]
        public void BlockBodyLambda_EmitsECSAVE001()
        {
            var result = Run(@"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => { return r.Target; });
    }
}");
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ECSAVE001"));
        }

        private static Microsoft.CodeAnalysis.GeneratorDriverRunResult Run(string src)
            => GeneratorTestHarness.Run(src);
    }
}
