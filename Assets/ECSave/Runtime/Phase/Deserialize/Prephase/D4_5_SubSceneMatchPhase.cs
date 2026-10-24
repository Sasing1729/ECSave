using System;
using System.Collections.Generic;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Entities;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Phase.Deserialize.Prephase
{
    internal static unsafe class D4_5_SubSceneMatchPhase
    {
        /// <summary>
        /// 検証付き版。Warnings を渡すとコンテンツハッシュ不一致時に SceneContentHashMismatch を追加する。
        /// </summary>
        public static SaveLoadErrorCode Execute(
            SubSceneDefinition def,
            SectionTable table,
            ByteView fileView,
            WarningCollector warnings,
            out SubSceneStateSectionView parsed,
            out bool hasSubSceneSection)
        {
            parsed = default;
            hasSubSceneSection = false;

            if (def == null || !def.RegisterSceneLoadState) return SaveLoadErrorCode.None;
            if (table.Entries == null) return SaveLoadErrorCode.None;

            for (int i = 0; i < table.Count; i++)
            {
                var e = table.Entries[i];
                if (e.Kind != SectionKind.SubSceneState) continue;

                if (e.Offset > int.MaxValue || e.Length > int.MaxValue)
                    return SaveLoadErrorCode.CorruptedData;

                int offset = (int)e.Offset;
                int length = (int)e.Length;

                if (!fileView.BoundsCheck(offset, length))
                    return SaveLoadErrorCode.CorruptedData;

                SubSceneStateSectionView view;
                try
                {
                    view = SubSceneStateSection.Read(fileView.Pointer + offset, length);
                }
                catch (FormatException)
                {
                    return SaveLoadErrorCode.CorruptedData;
                }

                parsed = view;
                hasSubSceneSection = true;

                if (def.ContentVerification == ContentVerificationMode.GuidAndContentHash)
                    VerifyContentHashes(def, view, warnings);

                return SaveLoadErrorCode.None;
            }

            return SaveLoadErrorCode.None;
        }

        /// <summary>
        /// 検証なし版（後方互換）。既存テストとの互換のために残す。
        /// </summary>
        public static SaveLoadErrorCode Execute(
            SubSceneDefinition def,
            SectionTable table,
            ByteView fileView,
            out SubSceneStateSectionView parsed,
            out bool hasSubSceneSection)
        {
            var dummy = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                return Execute(def, table, fileView, dummy, out parsed, out hasSubSceneSection);
            }
            finally { dummy.Dispose(); }
        }

        private static void VerifyContentHashes(
            SubSceneDefinition def,
            in SubSceneStateSectionView saved,
            WarningCollector warnings)
        {
            if (!saved.HasContentHash) return;
            if (def.SceneStateProvider == null) return;

            IReadOnlyList<SceneLoadInfo> current;
            try
            {
                current = def.SceneStateProvider() ?? Array.Empty<SceneLoadInfo>();
            }
            catch
            {
                return;
            }

            var currentHashes = new Dictionary<SceneGuid, ulong>(current.Count);
            for (int i = 0; i < current.Count; i++)
            {
                var s = current[i];
                if (s.ContentHash != 0UL)
                    currentHashes[s.Guid] = s.ContentHash;
            }

            if (currentHashes.Count == 0) return;

            for (int i = 0; i < saved.Count; i++)
            {
                ulong savedHash = SubSceneStateSection.GetContentHash(saved, i);
                if (savedHash == 0UL) continue;

                var guid = SubSceneStateSection.GetGuid(saved, i);
                if (!currentHashes.TryGetValue(guid, out var currentHash)) continue;
                if (currentHash == 0UL) continue;

                if (savedHash != currentHash)
                {
                    warnings.Add(WarningRecord.Create(
                        LoadWarningKind.SceneContentHashMismatch,
                        typeSectionId: 0,
                        saveEntityIndex: i,
                        sourceEntity: Entity.Null));
                }
            }
        }
    }
}
