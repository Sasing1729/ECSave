namespace ECSave.Internal
{
    public abstract class PrefabMigrationStepBase
    {
        public abstract ulong OldPrefabSchemaHash { get; }
        public abstract ulong NewPrefabSchemaHash { get; }
        public abstract void Migrate(ref global::ECSave.PrefabMigrationContext context);
    }
}
