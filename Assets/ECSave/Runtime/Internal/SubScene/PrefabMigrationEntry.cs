namespace ECSave.Internal
{
    internal struct PrefabMigrationEntry
    {
        public uint PersistentId;
        public ulong SourcePrefabGuid;
        public ulong SourceSchemaHash;
        public PrefabMigrationStepBase[] Steps;
    }
}
