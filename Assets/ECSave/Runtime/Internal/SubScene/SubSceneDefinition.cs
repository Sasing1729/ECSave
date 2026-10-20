using System;
using System.Collections.Generic;
using ECSave.Internal;

namespace ECSave.Internal.SubScene
{
    public sealed class SubSceneDefinition
    {
        public bool RegisterSceneLoadState { get; }
        public bool RegisterDestroyedEntities { get; }
        public bool HasSceneRestoreCallback { get; }
        public bool WithSceneStateRollback { get; }
        public ContentVerificationMode ContentVerification { get; }
        public StreamingConsistencyMode StreamingConsistency { get; }
        public int TombstoneCapacity { get; }
        public Func<IReadOnlyList<SceneLoadInfo>> SceneStateProvider { get; }
        public Func<SceneLoadRequest[], SceneLoadOutcome[]> SceneRestoreCallback { get; }
        internal IReadOnlyList<SubSceneDestroyedIdentityHandler> DestroyedHandlers { get; }
        public IReadOnlyList<PrefabMigrationStepBase> PrefabMigrations { get; }

        public static readonly SubSceneDefinition Empty = new SubSceneDefinition(
            false, false, false, false,
            ContentVerificationMode.None,
            StreamingConsistencyMode.TimeBased,
            0, null, null,
            Array.Empty<SubSceneDestroyedIdentityHandler>(),
            Array.Empty<PrefabMigrationStepBase>());

        // 後方互換用（旧 10 引数）。destroyedHandlers を空として新コンストラクタへ委譲する。
        public SubSceneDefinition(
            bool registerSceneLoadState,
            bool registerDestroyedEntities,
            bool hasSceneRestoreCallback,
            bool withSceneStateRollback,
            ContentVerificationMode contentVerification,
            StreamingConsistencyMode streamingConsistency,
            int tombstoneCapacity,
            Func<IReadOnlyList<SceneLoadInfo>> sceneStateProvider,
            Func<SceneLoadRequest[], SceneLoadOutcome[]> sceneRestoreCallback,
            IReadOnlyList<PrefabMigrationStepBase> prefabMigrations)
            : this(
                registerSceneLoadState,
                registerDestroyedEntities,
                hasSceneRestoreCallback,
                withSceneStateRollback,
                contentVerification,
                streamingConsistency,
                tombstoneCapacity,
                sceneStateProvider,
                sceneRestoreCallback,
                Array.Empty<SubSceneDestroyedIdentityHandler>(),
                prefabMigrations)
        {
        }

        // 完全版（S1b 内部使用）
        internal SubSceneDefinition(
            bool registerSceneLoadState,
            bool registerDestroyedEntities,
            bool hasSceneRestoreCallback,
            bool withSceneStateRollback,
            ContentVerificationMode contentVerification,
            StreamingConsistencyMode streamingConsistency,
            int tombstoneCapacity,
            Func<IReadOnlyList<SceneLoadInfo>> sceneStateProvider,
            Func<SceneLoadRequest[], SceneLoadOutcome[]> sceneRestoreCallback,
            IReadOnlyList<SubSceneDestroyedIdentityHandler> destroyedHandlers,
            IReadOnlyList<PrefabMigrationStepBase> prefabMigrations)
        {
            RegisterSceneLoadState = registerSceneLoadState;
            RegisterDestroyedEntities = registerDestroyedEntities;
            HasSceneRestoreCallback = hasSceneRestoreCallback;
            WithSceneStateRollback = withSceneStateRollback;
            ContentVerification = contentVerification;
            StreamingConsistency = streamingConsistency;
            TombstoneCapacity = tombstoneCapacity;
            SceneStateProvider = sceneStateProvider;
            SceneRestoreCallback = sceneRestoreCallback;
            DestroyedHandlers = destroyedHandlers ?? Array.Empty<SubSceneDestroyedIdentityHandler>();
            PrefabMigrations = prefabMigrations ?? Array.Empty<PrefabMigrationStepBase>();
        }

        public bool HasAnyConfiguration
            => RegisterSceneLoadState
            || RegisterDestroyedEntities
            || HasSceneRestoreCallback
            || WithSceneStateRollback
            || ContentVerification != ContentVerificationMode.None
            || StreamingConsistency != StreamingConsistencyMode.TimeBased
            || TombstoneCapacity > 0
            || DestroyedHandlers.Count > 0
            || PrefabMigrations.Count > 0;

        public bool HasTombstones
            => RegisterDestroyedEntities && DestroyedHandlers.Count > 0;
    }
}
