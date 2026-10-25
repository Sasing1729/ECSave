using System;
using System.Collections.Generic;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// <c>DeserializeOperation.DoApply</c> から呼ばれる共通ヘルパー群。
    ///
    /// <para>
    /// <b>責務</b>: Apply フェーズの事前検証と、シーン状態のスナップショット／ロールバック。
    /// いずれも「変更適用の前後を支える」処理であり、Phase のステップ本体ではない。
    /// </para>
    /// </summary>
    internal static class DeserializeApplyHelper
    {
        /// <summary>
        /// Update 対象の Entity が World にまだ存在するかを検証する。
        /// 存在しない場合、何らかの理由で外部から破棄された（想定外）ため失敗扱い。
        /// </summary>
        public static SaveLoadErrorCode ValidateUpdateTargets(
            EntityManager em, NativeList<Entity> updateTargets)
        {
            if (!updateTargets.IsCreated) return SaveLoadErrorCode.None;

            for (int i = 0; i < updateTargets.Length; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null) continue;
                if (!em.Exists(e))
                    return SaveLoadErrorCode.WorldModificationFailed;
            }
            return SaveLoadErrorCode.None;
        }

        /// <summary>
        /// シーン状態のスナップショットを取得する。
        ///
        /// <para>
        /// <see cref="SubSceneDefinition.WithSceneStateRollback"/> が有効で、
        /// <c>SceneStateProvider</c> / <c>SceneRestoreCallback</c> が指定されている場合のみ
        /// スナップショットを取得する。それ以外は <c>null</c> を返し、
        /// <see cref="TryRollbackSceneState"/> は「ロールバック不要」として扱う。
        /// </para>
        /// </summary>
        public static IReadOnlyList<SceneLoadInfo> CaptureSceneSnapshot(SubSceneDefinition def)
        {
            if (def == null) return null;
            if (!def.WithSceneStateRollback) return null;
            if (def.SceneStateProvider == null) return null;
            if (def.SceneRestoreCallback == null) return null;

            try
            {
                return def.SceneStateProvider() ?? Array.Empty<SceneLoadInfo>();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ECSave] Failed to capture scene snapshot: {ex}");
                return null;
            }
        }

        /// <summary>
        /// シーン状態のロールバックを試みる。
        ///
        /// <para>
        /// スナップショットが取れている場合はそれを目標状態とし、取れていない場合は
        /// 実行済みリクエストの逆操作を目標状態とする（best effort）。
        /// </para>
        /// <para>
        /// すべての復元が成功した場合、<c>SceneStateRolledBack</c> 警告を追加する。
        /// </para>
        /// </summary>
        public static bool TryRollbackSceneState(
            IReadOnlyList<SceneLoadInfo> snapshot,
            SceneLoadRequest[] executedRequests,
            SubSceneDefinition def,
            ref WarningCollector warnings)
        {
            if (def == null || !def.WithSceneStateRollback) return true;
            if (def.SceneRestoreCallback == null) return true;

            try
            {
                IReadOnlyList<SceneLoadInfo> target = snapshot;
                if (target == null && executedRequests != null && executedRequests.Length > 0)
                {
                    var inverse = new SceneLoadInfo[executedRequests.Length];
                    for (int i = 0; i < executedRequests.Length; i++)
                    {
                        var r = executedRequests[i];
                        var invState = r.TargetState == SceneLoadState.Loaded
                            ? SceneLoadState.Unloaded
                            : SceneLoadState.Loaded;
                        inverse[i] = new SceneLoadInfo(r.Guid, invState, r.Mode, r.ContentHash);
                    }
                    target = inverse;
                }

                if (target == null || target.Count == 0) return true;

                var current = new Dictionary<SceneGuid, SceneLoadState>();
                if (def.SceneStateProvider != null)
                {
                    try
                    {
                        var cur = def.SceneStateProvider();
                        if (cur != null)
                        {
                            for (int i = 0; i < cur.Count; i++)
                                current[cur[i].Guid] = cur[i].State;
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError(
                            $"[ECSave] SceneStateProvider threw during rollback: {ex}");
                    }
                }

                var restore = new List<SceneLoadRequest>();
                for (int i = 0; i < target.Count; i++)
                {
                    var s = target[i];
                    if (current.TryGetValue(s.Guid, out var curState) && curState == s.State) continue;
                    restore.Add(new SceneLoadRequest(s.Guid, s.State, s.RestoreMode, s.ContentHash));
                }

                if (restore.Count == 0) return true;

                var outcomes = def.SceneRestoreCallback(restore.ToArray());
                if (outcomes == null) return true;

                bool allOk = true;
                int limit = Math.Min(outcomes.Length, restore.Count);
                for (int i = 0; i < limit; i++)
                {
                    if (!outcomes[i].Success)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[ECSave] Scene rollback failed for {restore[i].Guid}: {outcomes[i].FailureReason}");
                        allOk = false;
                    }
                }

                if (allOk)
                {
                    warnings.Add(WarningRecord.Create(
                        LoadWarningKind.SceneStateRolledBack, 0, -1, Entity.Null));
                }
                return allOk;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ECSave] Scene rollback threw: {ex}");
                return false;
            }
        }
    }
}
