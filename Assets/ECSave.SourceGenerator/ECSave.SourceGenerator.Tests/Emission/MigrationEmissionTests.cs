using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class MigrationEmissionTests
    {
        [Test]
        public void Generator_EmitsMigrationStep_WithFields()
        {
            var src = @"
using ECSave;
using ECSave.Internal;
using Unity.Entities;

public struct HealthV1 : IComponentData { public int Value; }
public struct HealthV2 : IComponentData { public int Current; public int Max; }

public sealed class HealthMig : IComponentMigration<HealthV1, HealthV2>
{
    public HealthV2 Migrate(in HealthV1 old) { return default(HealthV2); }
}

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterMigration<HealthMig>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthMigStep.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("HealthMigStep"));
            Assert.IsTrue(text.Contains("old.Value = reader.ReadI32()"));
            Assert.IsTrue(text.Contains("writer.WriteI32(@new.Current)"));
            Assert.IsTrue(text.Contains("writer.WriteI32(@new.Max)"));
        }

        [Test]
        public void Generator_EmitsMigrationStep_WithHashes()
        {
            var src = @"
using ECSave;
using ECSave.Internal;
using Unity.Entities;

public struct Old : IComponentData { public int V; }
public struct New : IComponentData { public int V; }

public sealed class M : IComponentMigration<Old, New>
{
    public New Migrate(in Old old) { return default(New); }
}

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterMigration<M>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "MStep.g.cs");
            Assert.IsNotNull(text);
            Assert.IsFalse(text.Contains("0x0000000000000000UL"),
                "hash should not be zero for identical layouts");
        }

        [Test]
        public void Generator_EmitsMigrationStep_FloatFields()
        {
            var src = @"
using ECSave;
using ECSave.Internal;
using Unity.Entities;

public struct Old : IComponentData { public float V; }
public struct New : IComponentData { public double V; }

public sealed class M : IComponentMigration<Old, New>
{
    public New Migrate(in Old old) { return default(New); }
}

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterMigration<M>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "MStep.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("UnsafeUtility.As<uint, float>"));
            Assert.IsTrue(text.Contains("UnsafeUtility.As<double, ulong>"));
        }

        [Test]
        public void Generator_NoEmitMigrationStep_WithoutTargetTypes()
        {
            var src = @"
using ECSave;

public sealed class MyMig
{
    public MyMig() { }
}

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterMigration<MyMig>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "MyMigStep.g.cs");
            Assert.IsNull(text);
        }
    }
}
