using System;
using System.Collections.Generic;
using ECSave;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public sealed class GenSubSceneScope : SaveScope
    {
        public static IReadOnlyList<SceneLoadInfo> NextProviderResult = Array.Empty<SceneLoadInfo>();
        public static Func<SceneLoadRequest[], SceneLoadOutcome[]> NextCallback;

        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenSubSceneWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.RegisterSceneLoadState()
             .WithSceneStateProvider(() => NextProviderResult)
             .WithSceneRestoreCallback(reqs => NextCallback != null
                 ? NextCallback(reqs)
                 : MakeAllOk(reqs));
        }

        private static SceneLoadOutcome[] MakeAllOk(SceneLoadRequest[] reqs)
        {
            var outcomes = new SceneLoadOutcome[reqs.Length];
            for (int i = 0; i < outcomes.Length; i++)
                outcomes[i] = SceneLoadOutcome.Ok();
            return outcomes;
        }
    }

    public sealed class GenPrefabMigration : IPrefabMigration
    {
        public static int InvokeCount;

        public ulong OldPrefabSchemaHash => 0xA1;
        public ulong NewPrefabSchemaHash => 0xB2;

        public void Migrate(ref PrefabMigrationContext context)
        {
            InvokeCount++;
        }
    }

    public sealed class GenPrefabMigrationScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenPrefabMigrationWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.RegisterPrefabMigration<GenPrefabMigration>();
        }
    }

    public sealed class GenSubSceneWithoutCallbackScope : SaveScope
    {
        public static IReadOnlyList<SceneLoadInfo> NextProviderResult = System.Array.Empty<SceneLoadInfo>();

        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenSubSceneNoCbWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.RegisterSceneLoadState()
             .WithSceneStateProvider(() => NextProviderResult);
        }
    }
}
