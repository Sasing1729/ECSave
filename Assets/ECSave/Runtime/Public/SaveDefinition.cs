using System;
using System.Collections.Generic;
using ECSave.Format;
using ECSave.Internal;

namespace ECSave
{
    public sealed class SaveDefinition
    {
        public IReadOnlyList<SaveTypeModule> Modules { get; }
        public int GameDataVersion { get; }
        public string WorldId { get; }
        public MigrationTable Migrations { get; set; }
        public IReadOnlyList<IdentityModule> Identities { get; }
        public IReadOnlyList<SelectionModule> Selections { get; }

        public SaveDefinition(
            IReadOnlyList<SaveTypeModule> modules,
            int gameDataVersion,
            string worldId)
            : this(modules, null, null, gameDataVersion, worldId)
        {
        }

        public SaveDefinition(
            IReadOnlyList<SaveTypeModule> modules,
            IReadOnlyList<IdentityModule> identities,
            int gameDataVersion,
            string worldId)
            : this(modules, identities, null, gameDataVersion, worldId)
        {
        }

        public SaveDefinition(
            IReadOnlyList<SaveTypeModule> modules,
            IReadOnlyList<IdentityModule> identities,
            IReadOnlyList<SelectionModule> selections,
            int gameDataVersion,
            string worldId)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            if (gameDataVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(gameDataVersion));

            for (int i = 0; i < modules.Count; i++)
                modules[i].RegistrationId = i;

            if (identities != null)
                for (int i = 0; i < identities.Count; i++)
                    identities[i].RegistrationId = i;

            if (selections != null)
                for (int i = 0; i < selections.Count; i++)
                    selections[i].RegistrationId = i;

            Modules = modules;
            GameDataVersion = gameDataVersion;
            WorldId = worldId ?? string.Empty;
            Migrations = null;
            Identities = identities;
            Selections = selections;
        }

        public IdentityModule FindIdentity(ulong nameHash)
        {
            if (Identities == null) return null;

            for (int i = 0; i < Identities.Count; i++)
            {
                var m = Identities[i];
                if (SchemaHashCore.HashUtf8(m.FullTypeName) == nameHash)
                    return m;
            }

            return null;
        }
    }
}
