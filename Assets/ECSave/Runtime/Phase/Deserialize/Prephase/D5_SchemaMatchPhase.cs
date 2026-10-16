using ECSave.Format;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class D5_SchemaMatchPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            var def = ctx.Definition;
            int count = ctx.SectionTable.Count;
            ctx.ModuleBySection = new SaveTypeModule[count];

            int moduleCount = def.Modules.Count;
            var hashes = new ulong[moduleCount];
            for (int k = 0; k < moduleCount; k++)
                hashes[k] = SchemaHashCore.HashUtf8(def.Modules[k].FullTypeName);

            var claimed = new bool[moduleCount];
            var table = def.Migrations;

            for (int i = 0; i < count; i++)
            {
                ref var entry = ref ctx.SectionTable.Entries[i];

                if (entry.Kind == SectionKind.Identity)
                    continue;

                int idx = IndexOf(hashes, entry.NameHash);
                if (idx < 0 && table != null
                    && table.TryResolveRenamed(entry.NameHash, out ulong renamedHash))
                    idx = IndexOf(hashes, renamedHash);

                if (idx < 0)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.UnknownSectionIgnored,
                        (uint)i, -1, Entity.Null));
                    continue;
                }

                var m = def.Modules[idx];
                if (m.SectionKind != entry.Kind)
                    return SaveLoadErrorCode.SchemaMismatch;

                if (claimed[idx])
                    return SaveLoadErrorCode.SchemaMismatch;
                claimed[idx] = true;

                ctx.ModuleBySection[i] = m;
            }

            return SaveLoadErrorCode.None;
        }

        private static int IndexOf(ulong[] hashes, ulong nameHash)
        {
            for (int i = 0; i < hashes.Length; i++)
                if (hashes[i] == nameHash) return i;
            return -1;
        }
    }
}
