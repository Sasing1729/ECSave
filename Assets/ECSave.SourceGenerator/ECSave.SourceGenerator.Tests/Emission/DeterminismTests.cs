using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class DeterminismTests
    {
        [Test]
        public void SameInput_SameOutput()
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
            var r1 = GeneratorTestHarness.Run(src);
            var r2 = GeneratorTestHarness.Run(src);

            var t1 = GeneratorTestHarness.GetGenerated(r1, "HealthModule.g.cs");
            var t2 = GeneratorTestHarness.GetGenerated(r2, "HealthModule.g.cs");
            Assert.AreEqual(t1, t2);
        }

        [Test]
        public void SameInput_SameSchemaHash()
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
            var r1 = GeneratorTestHarness.Run(src);
            var r2 = GeneratorTestHarness.Run(src);

            var t1 = GeneratorTestHarness.GetGenerated(r1, "HealthModule.g.cs");
            var t2 = GeneratorTestHarness.GetGenerated(r2, "HealthModule.g.cs");
            Assert.AreEqual(t1, t2);
        }

        [Test]
        public void DifferentInput_DifferentSchemaHash()
        {
            var src1 = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int Current; public int Max; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}";
            var src2 = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int Current; public int Max; public int Extra; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}";
            var r1 = GeneratorTestHarness.Run(src1);
            var r2 = GeneratorTestHarness.Run(src2);

            var t1 = GeneratorTestHarness.GetGenerated(r1, "HealthModule.g.cs");
            var t2 = GeneratorTestHarness.GetGenerated(r2, "HealthModule.g.cs");
            Assert.AreNotEqual(t1, t2);
        }
    }
}
