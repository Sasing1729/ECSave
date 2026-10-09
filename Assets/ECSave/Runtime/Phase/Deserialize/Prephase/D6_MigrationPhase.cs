namespace ECSave.Internal
{
    internal static unsafe class D6_MigrationPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;

                ref var entry = ref ctx.SectionTable.Entries[i];
                if (m.SchemaHash == entry.SchemaHash) continue;

                if (ctx.Definition.Migrations == null)
                    return SaveLoadErrorCode.SchemaMismatch;

                var step = ctx.Definition.Migrations.Resolve(m.SchemaHash, entry.SchemaHash);
                if (step == null)
                    return SaveLoadErrorCode.SchemaMismatch;

                return SaveLoadErrorCode.MigrationFailed;
            }

            return SaveLoadErrorCode.None;
        }
    }
}
