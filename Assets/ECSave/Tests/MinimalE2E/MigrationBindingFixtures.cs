using ECSave;
using ECSave.Format;
using ECSave.Internal;

namespace Game.MinimalE2E
{
    internal sealed class StaleHealthMigration : MigrationStepBase
    {
        public const ulong BadNewHash = 0xBADBADBADBADBADBUL;

        public override ulong NewTypeNameHash => HealthV1ToV2Migration.NameHash;
        public override ulong OldSchemaHash => HealthV1ToV2Migration.OldHash;
        public override ulong NewSchemaHash => BadNewHash;
        public override void Migrate(ref ByteReader reader, ref ByteWriter writer) { }
    }

    internal sealed class HealthV0ToV2Migration : MigrationStepBase
    {
        public const ulong OldHash = 0x0123456789ABCDEFUL;

        public override ulong NewTypeNameHash => HealthV1ToV2Migration.NameHash;
        public override ulong OldSchemaHash => OldHash;
        public override ulong NewSchemaHash => HealthV1ToV2Migration.NewHash;
        public override void Migrate(ref ByteReader reader, ref ByteWriter writer) { }
    }

    public sealed class MigrationOkScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder builder)
        {
            var impl = (SaveDefinitionBuilderImpl)builder;
            impl.RegisterModule(new HealthModule());
            impl.RegisterMigrationStep(new HealthV1ToV2Migration());
            builder.WorldId("MigrationOkWorld");
        }
    }

    public sealed class MigrationStaleScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder builder)
        {
            var impl = (SaveDefinitionBuilderImpl)builder;
            impl.RegisterModule(new HealthModule());
            impl.RegisterMigrationStep(new StaleHealthMigration());
            builder.WorldId("MigrationStaleWorld");
        }
    }

    public sealed class MigrationOrphanScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder builder)
        {
            var impl = (SaveDefinitionBuilderImpl)builder;
            impl.RegisterModule(new StunnedModule());
            impl.RegisterMigrationStep(new HealthV1ToV2Migration());
            builder.WorldId("MigrationOrphanWorld");
        }
    }

    public sealed class MigrationUnknownScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder builder)
        {
            var impl = (SaveDefinitionBuilderImpl)builder;
            impl.RegisterMigrationStep(new HealthV1ToV2Migration());
            builder.WorldId("MigrationUnknownWorld");
        }
    }

    public sealed class MigrationMultiScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder builder)
        {
            var impl = (SaveDefinitionBuilderImpl)builder;
            impl.RegisterModule(new HealthModule());
            impl.RegisterMigrationStep(new HealthV1ToV2Migration());
            impl.RegisterMigrationStep(new HealthV0ToV2Migration());
            builder.WorldId("MigrationMultiWorld");
        }
    }

    public sealed class MigrationBrokenScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder builder)
        {
            throw new System.InvalidOperationException("broken scope");
        }
    }
}
