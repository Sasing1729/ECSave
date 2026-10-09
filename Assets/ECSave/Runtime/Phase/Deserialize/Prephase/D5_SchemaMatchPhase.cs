using ECSave.Format;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class D5_SchemaMatchPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            int count = ctx.SectionTable.Count;
            ctx.ModuleBySection = new SaveTypeModule[count];

            for (int i = 0; i < count; i++)
            {
                ref var entry = ref ctx.SectionTable.Entries[i];

                if (entry.Kind == SectionKind.Identity)
                    continue;

                var m = FindModuleByNameHash(ctx.Definition, entry.NameHash);
                if (m == null)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.UnknownSectionIgnored,
                        (uint)i, -1, Entity.Null));
                    continue;
                }

                if (m.SectionKind != entry.Kind)
                    return SaveLoadErrorCode.SchemaMismatch;

                ctx.ModuleBySection[i] = m;
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveTypeModule FindModuleByNameHash(SaveDefinition def, ulong nameHash)
        {
            for (int i = 0; i < def.Modules.Count; i++)
            {
                var m = def.Modules[i];
                if (SchemaHashCore.HashUtf8(m.FullTypeName) == nameHash)
                    return m;
            }
            return null;
        }
    }
}
