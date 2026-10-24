using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using System;
using System.Collections.Generic;

namespace ECSave
{
    public sealed class SaveDefinition
    {
        public IReadOnlyList<SaveTypeModule> Modules { get; }
        public IReadOnlyList<IdentityModule> Identities { get; }
        public IReadOnlyList<SelectionModule> Selections { get; }
        public int GameDataVersion { get; }
        public string WorldId { get; }
        public MigrationTable Migrations { get; internal set; }
        public SubSceneDefinition SubScene { get; internal set; }

        /// <summary>
        /// ★ 追加: Deserialize 時に FixedString の切詰め疑いを検出したら
        /// 文字境界まで自動修復するか。デフォルト true。
        /// 無効化するには SaveDefinitionBuilderImpl.DisableAutoRepairFixedString() を使う。
        /// </summary>
        public bool AutoRepairFixedString { get; internal set; } = true;

        public SaveDefinition(
            IReadOnlyList<SaveTypeModule> modules,
            int gameDataVersion,
            string worldId)
            : this(
                modules,
                Array.Empty<IdentityModule>(),
                Array.Empty<SelectionModule>(),
                gameDataVersion,
                worldId)
        {
        }

        public SaveDefinition(
            IReadOnlyList<SaveTypeModule> modules,
            IReadOnlyList<IdentityModule> identities,
            int gameDataVersion,
            string worldId)
            : this(
                modules,
                identities,
                Array.Empty<SelectionModule>(),
                gameDataVersion,
                worldId)
        {
        }

        public SaveDefinition(
            IReadOnlyList<SaveTypeModule> modules,
            IReadOnlyList<IdentityModule> identities,
            IReadOnlyList<SelectionModule> selections,
            int gameDataVersion,
            string worldId)
        {
            Modules = modules ?? throw new ArgumentNullException(nameof(modules));
            Identities = identities ?? Array.Empty<IdentityModule>();
            Selections = selections ?? Array.Empty<SelectionModule>();
            GameDataVersion = gameDataVersion;
            WorldId = worldId ?? string.Empty;
            SubScene = SubSceneDefinition.Empty;
        }

        public IdentityModule FindIdentity(ulong nameHash)
        {
            for (int i = 0; i < Identities.Count; i++)
            {
                var im = Identities[i];
                if (SchemaHashCore.HashUtf8(im.FullTypeName) == nameHash)
                    return im;
            }
            return null;
        }
    }
}
