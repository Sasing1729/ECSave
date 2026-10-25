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
        /// 検証付き版。<paramref name="warnings"/> を渡すと、
        /// <see cref="ContentVerificationMode.GuidOnly"/> /
        /// <see cref="ContentVerificationMode.GuidAndContentHash"/> の指定に応じて
        /// GUID/State の一致検証と ContentHash の一致検証を行う。
        ///
        /// <para>
        /// <b>検証モードごとの動作</b>:
        /// <list type="bullet">
        ///   <item><c>None</c>: 検証なし</item>
        ///   <item>
        ///     <c>GuidOnly</c>: 保存時と現在の GUID および <see cref="SceneLoadState"/> を比較。
        ///     不一致なら <see cref="LoadWarningKind.SceneLoadStateMismatch"/> を記録。
        ///     <see cref="SceneLoadState.Loading"/> は一時状態のため比較対象外。
        ///     <see cref="SceneLoadInfo.ContentHash"/> は **検証しない**
        ///     （計算コストを払わない中間モード）。
        ///   </item>
        ///   <item>
        ///     <c>GuidAndContentHash</c>: 上記に加え、
        ///     <see cref="SceneLoadInfo.ContentHash"/> も比較。
        ///     不一致なら <see cref="LoadWarningKind.SceneContentHashMismatch"/> を記録。
        ///   </item>
        /// </list>
        /// </para>
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

                switch (def.ContentVerification)
                {
                    case ContentVerificationMode.GuidOnly:
                        VerifyGuidsAndStates(def, view, warnings);
                        break;

                    case ContentVerificationMode.GuidAndContentHash:
                        VerifyGuidsAndStates(def, view, warnings);
                        VerifyContentHashes(def, view, warnings);
                        break;
                }

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

        /// <summary>
        /// GUID と <see cref="SceneLoadState"/> の一致検証。
        ///
        /// <para>
        /// <b>検証の内容</b>:
        /// <list type="bullet">
        ///   <item>保存時に存在した GUID が現在の Provider に無い → 警告</item>
        ///   <item>両方に存在するが State が異なる → 警告</item>
        ///   <item>どちらかが <see cref="SceneLoadState.Loading"/> → 比較対象外
        ///         （一時状態のため）</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>警告の種類</b>: <see cref="LoadWarningKind.SceneLoadStateMismatch"/>。
        /// この enum は <c>GuidOnly</c> の実装に伴い、本来の用途で使用される。
        /// </para>
        /// </summary>
        private static void VerifyGuidsAndStates(
            SubSceneDefinition def,
            in SubSceneStateSectionView saved,
            WarningCollector warnings)
        {
            if (def.SceneStateProvider == null) return;
            if (saved.Count == 0) return;

            IReadOnlyList<SceneLoadInfo> current;
            try
            {
                current = def.SceneStateProvider() ?? Array.Empty<SceneLoadInfo>();
            }
            catch
            {
                return;
            }

            // GUID → 現在の State
            var currentStates = new Dictionary<SceneGuid, SceneLoadState>(current.Count);
            for (int i = 0; i < current.Count; i++)
            {
                var s = current[i];
                currentStates[s.Guid] = s.State;
            }

            for (int i = 0; i < saved.Count; i++)
            {
                var savedState = SubSceneStateSection.GetState(saved, i);

                // Loading は一時状態のため比較対象外
                if (savedState == SceneLoadState.Loading) continue;

                var guid = SubSceneStateSection.GetGuid(saved, i);

                if (!currentStates.TryGetValue(guid, out var currentState))
                {
                    // 保存時に存在したシーンが現在の Provider に無い
                    warnings.Add(WarningRecord.Create(
                        LoadWarningKind.SceneLoadStateMismatch,
                        typeSectionId: 0,
                        saveEntityIndex: i,
                        sourceEntity: Entity.Null));
                    continue;
                }

                if (currentState == SceneLoadState.Loading) continue;

                if (savedState != currentState)
                {
                    warnings.Add(WarningRecord.Create(
                        LoadWarningKind.SceneLoadStateMismatch,
                        typeSectionId: 0,
                        saveEntityIndex: i,
                        sourceEntity: Entity.Null));
                }
            }
        }

        /// <summary>
        /// <see cref="SceneLoadInfo.ContentHash"/> の一致検証。
        /// <see cref="ContentVerificationMode.GuidAndContentHash"/> のときのみ呼ばれる。
        /// </summary>
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
