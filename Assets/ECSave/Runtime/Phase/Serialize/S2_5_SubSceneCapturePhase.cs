using System;
using System.Collections.Generic;
using ECSave.Internal.SubScene;

namespace ECSave.Phase.Serialize
{
    internal static class S2_5_SubSceneCapturePhase
    {
        public sealed class Result
        {
            public bool HasSection { get; set; }
            public int SectionBodySize { get; set; }
            public IReadOnlyList<SceneLoadInfo> Scenes { get; set; } = Array.Empty<SceneLoadInfo>();
        }

        public static Result Execute(SubSceneDefinition def)
        {
            var result = new Result();
            if (def == null || !def.RegisterSceneLoadState) return result;

            IReadOnlyList<SceneLoadInfo> scenes = Array.Empty<SceneLoadInfo>();

            if (def.SceneStateProvider != null)
            {
                try
                {
                    scenes = def.SceneStateProvider() ?? Array.Empty<SceneLoadInfo>();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[ECSave] SceneStateProvider threw: {ex}");
                    scenes = Array.Empty<SceneLoadInfo>();
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    "[ECSave] RegisterSceneLoadState is enabled but no SceneStateProvider was provided. " +
                    "The SubScene state section will be written as empty.");
            }

            result.HasSection = true;
            result.Scenes = scenes;
            result.SectionBodySize = SubSceneStateLayout.ComputeBodySize(scenes);
            return result;
        }
    }
}
