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
        /// Deserialize 時に FixedString の切詰め疑いを検出したら
        /// 文字境界まで自動修復するか。デフォルト true。
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

            // Review #8a: 直接コンストラクタで生成された場合、RegistrationId が
            // 全て 0 のままになり、複数モジュールで P4_StructuralChangePhase の
            // CollectCoveredPids が誤動作する（被覆判定が崩れてコンポーネントを
            // 誤って外す）。IdentityIndex の typeIndex も全て 0 になり、
            // 異なる Identity 型の同値キーが衝突する（Review #8b）。
            //
            // SaveDefinitionBuilderImpl 経由の場合は既に 0..n-1 が振られているが、
            // 同じ値になるため上書きは無害。念のため明示的に振り直す。
            AssignRegistrationIds(Modules, static (m, i) => m.RegistrationId = i);
            AssignRegistrationIds(Identities, static (m, i) => m.RegistrationId = i);
            AssignRegistrationIds(Selections, static (m, i) => m.RegistrationId = i);
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

        /// <summary>
        /// リスト内の全要素に 0..n-1 の RegistrationId を振る。
        /// 空/null リストは何もしない。
        /// </summary>
        private static void AssignRegistrationIds<T>(
            IReadOnlyList<T> list, Action<T, int> assign)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                assign(list[i], i);
        }
    }
}
