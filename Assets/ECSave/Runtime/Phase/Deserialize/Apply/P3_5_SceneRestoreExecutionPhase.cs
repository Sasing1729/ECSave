using System;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Entities;

namespace ECSave.Phase.Deserialize.Apply
{
    internal static class P3_5_SceneRestoreExecutionPhase
    {
        public static SaveLoadErrorCode Execute(
            SubSceneDefinition def,
            SceneLoadRequest[] requests,
            ref WarningCollector warnings)
        {
            if (def == null || requests == null || requests.Length == 0)
                return SaveLoadErrorCode.None;

            if (def.SceneRestoreCallback == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[ECSave] Scene restore requested but no SceneRestoreCallback was provided. " +
                    "SubScene state will not be restored.");
                return SaveLoadErrorCode.None;
            }

            SceneLoadOutcome[] outcomes;
            try
            {
                outcomes = def.SceneRestoreCallback(requests);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ECSave] SceneRestoreCallback threw: {ex}");
                return SaveLoadErrorCode.SceneLoadFailed;
            }

            if (outcomes == null) return SaveLoadErrorCode.None;

            for (int i = 0; i < outcomes.Length && i < requests.Length; i++)
            {
                if (outcomes[i].Success) continue;

                if (requests[i].Mode == SceneRestoreMode.Required)
                {
                    // 修正: LogError → LogWarning（エラーコードで既に通知済み）
                    UnityEngine.Debug.LogWarning(
                        $"[ECSave] Required scene restore failed: {requests[i].Guid} ({outcomes[i].FailureReason})");
                    return SaveLoadErrorCode.SceneLoadFailed;
                }

                warnings.Add(WarningRecord.Create(
                    LoadWarningKind.SceneRestoreFailed,
                    typeSectionId: 0,
                    saveEntityIndex: -1,
                    sourceEntity: Entity.Null));
            }

            return SaveLoadErrorCode.None;
        }
    }
}
