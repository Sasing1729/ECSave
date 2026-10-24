using System.Collections.Generic;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Entities;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Phase.Deserialize.Prephase
{
    internal static unsafe class D5_5_PrefabMigrationPhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            out PrefabIdentitySectionView? parsed)
        {
            parsed = null;
            ctx.PrefabMigrationsToApply = null;

            var def = ctx.Definition.SubScene;
            if (def == null || !def.HasPrefabIdentity) return SaveLoadErrorCode.None;

            int found = -1;
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                if (ctx.SectionTable.Entries[i].Kind == SectionKind.PrefabIdentity) { found = i; break; }
            }
            if (found < 0) return SaveLoadErrorCode.None;

            ref var entry = ref ctx.SectionTable.Entries[found];
            if (entry.Offset > int.MaxValue || entry.Length > int.MaxValue)
                return SaveLoadErrorCode.CorruptedData;

            int offset = (int)entry.Offset;
            int length = (int)entry.Length;
            if (!ctx.View.BoundsCheck(offset, length))
                return SaveLoadErrorCode.CorruptedData;

            PrefabIdentitySectionView view;
            try
            {
                view = PrefabIdentitySection.Read(ctx.View.Pointer + offset, length);
            }
            catch (FormatException)
            {
                return SaveLoadErrorCode.CorruptedData;
            }

            parsed = view;

            if (def.PrefabIdentityProvider == null) return SaveLoadErrorCode.None;

            var table = PrefabMigrationTable.From(def.PrefabMigrations);
            bool migratingAtLeastOne = def.PrefabMigrations.Count > 0;
            List<PrefabMigrationEntry> toApply = null;
            HashSet<ulong> missingGuids = migratingAtLeastOne ? new HashSet<ulong>() : null;

            for (int i = 0; i < view.Count; i++)
            {
                uint pid = PrefabIdentitySection.GetEntityIndex(view, i);
                ulong prefabGuid = PrefabIdentitySection.GetPrefabGuid(view, i);
                ulong savedHash = PrefabIdentitySection.GetSchemaHash(view, i);

                if (prefabGuid == 0UL || savedHash == 0UL) continue;

                if (!PrefabSchemaRegistry.TryGet(prefabGuid, out ulong currentHash))
                {
                    if (migratingAtLeastOne && missingGuids.Add(prefabGuid))
                    {
                        ctx.Warnings.Add(WarningRecord.Create(
                            LoadWarningKind.PrefabSchemaNotRegistered,
                            typeSectionId: (uint)found,
                            saveEntityIndex: (int)pid,
                            sourceEntity: Entity.Null,
                            extraHash: prefabGuid));
                    }
                    continue;
                }

                if (currentHash == savedHash) continue;

                var chain = table.ResolveChain(savedHash, currentHash);
                if (chain == null || chain.Length == 0)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.PrefabSchemaMigrationFailed,
                        typeSectionId: 0,
                        saveEntityIndex: (int)pid,
                        sourceEntity: Entity.Null));
                    continue;
                }

                if (toApply == null) toApply = new List<PrefabMigrationEntry>();
                toApply.Add(new PrefabMigrationEntry
                {
                    PersistentId = pid,
                    SourcePrefabGuid = prefabGuid,
                    SourceSchemaHash = savedHash,
                    Steps = chain,
                });

                ctx.Warnings.Add(WarningRecord.Create(
                    LoadWarningKind.PrefabMigrated,
                    typeSectionId: 0,
                    saveEntityIndex: (int)pid,
                    sourceEntity: Entity.Null));
            }

            ctx.PrefabMigrationsToApply = toApply;
            return SaveLoadErrorCode.None;
        }
    }
}
