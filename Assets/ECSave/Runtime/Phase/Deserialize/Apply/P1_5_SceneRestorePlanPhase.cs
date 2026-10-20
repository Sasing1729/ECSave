using System;
using System.Collections.Generic;
using ECSave.Internal.SubScene;

namespace ECSave.Phase.Deserialize.Apply
{
    internal static class P1_5_SceneRestorePlanPhase
    {
        public static SaveLoadErrorCode Execute(
            SubSceneDefinition def,
            SubSceneStateSectionView? savedState,
            out SceneLoadRequest[] requests)
        {
            requests = Array.Empty<SceneLoadRequest>();

            if (def == null || !def.RegisterSceneLoadState) return SaveLoadErrorCode.None;
            if (savedState == null) return SaveLoadErrorCode.None;

            var view = savedState.Value;
            if (view.Count == 0) return SaveLoadErrorCode.None;

            var list = new List<SceneLoadRequest>((int)view.Count);
            for (int i = 0; i < view.Count; i++)
            {
                var info = SubSceneStateSection.GetSceneLoadInfo(view, i);
                list.Add(new SceneLoadRequest(info.Guid, info.State, info.RestoreMode));
            }

            requests = list.ToArray();
            return SaveLoadErrorCode.None;
        }
    }
}
