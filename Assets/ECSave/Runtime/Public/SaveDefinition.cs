using System;
using System.Collections.Generic;
using ECSave.Internal;

namespace ECSave
{
    public sealed class SaveDefinition
    {
        public IReadOnlyList<SaveTypeModule> Modules { get; }
        public int GameDataVersion { get; }
        public string WorldId { get; }
        public MigrationTable Migrations { get; }

        public SaveDefinition(
            IReadOnlyList<SaveTypeModule> modules,
            int gameDataVersion,
            string worldId)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            if (gameDataVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(gameDataVersion));

            for (int i = 0; i < modules.Count; i++)
                modules[i].RegistrationId = i;

            Modules = modules;
            GameDataVersion = gameDataVersion;
            WorldId = worldId ?? string.Empty;
            Migrations = null;
        }
    }
}
