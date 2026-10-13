using System.Linq;
using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class DiagnosticEmissionTests
    {
        [Test]
        public void PrivateField_EmitsECSAVE023()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { private int Hidden; public int Visible; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ECSAVE023"),
                "ECSAVE023 not found in: " +
                string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        [Test]
        public void AutoWithoutEntity_EmitsECSAVE029()
        {
            var src = @"
using ECSave;
using Unity.Entities;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public struct Auto : IComponentData { public int A; public int B; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Auto>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ECSAVE029"));
        }

        [Test]
        public void AutoWithEntity_EmitsECSAVE022()
        {
            var src = @"
using ECSave;
using Unity.Entities;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public struct WithEntity : IComponentData { public Entity E; public int V; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<WithEntity>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ECSAVE022"));
        }

        [Test]
        public void PackAttribute_EmitsECSAVE025()
        {
            var src = @"
using ECSave;
using Unity.Entities;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Packed : IComponentData { public int A; public byte B; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Packed>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ECSAVE025"));
        }

        [Test]
        public void DiagnosticEmittedOncePerLocation()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { private int Hidden; public int Visible; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
        b.RegisterComponent<Health>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            int count = result.Diagnostics.Count(d => d.Id == "ECSAVE023");
            Assert.AreEqual(1, count);
        }
    }
}
