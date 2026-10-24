using System;
using System.Collections.Generic;
using ECSave.Internal;
using Unity.Entities;

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
        public Func<Entity, PrefabIdentityInfo?> PrefabIdentityProvider { get; }
        internal IReadOnlyList<SubSceneDestroyedIdentityHandler> DestroyedHandlers { get; }
        public IReadOnlyList<PrefabMigrationStepBase> PrefabMigrations { get; }

        public static readonly SubSceneDefinition Empty = new SubSceneDefinition(
            false, false, false, false,
            ContentVerificationMode.None,
            StreamingConsistencyMode.TimeBased,
            0, null, null, null,
            Array.Empty<SubSceneDestroyedIdentityHandler>(),
            Array.Empty<PrefabMigrationStepBase>());

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
            : this(registerSceneLoadState, registerDestroyedEntities,
                   hasSceneRestoreCallback, withSceneStateRollback,
                   contentVerification, streamingConsistency,
                   tombstoneCapacity, sceneStateProvider, sceneRestoreCallback,
                   null,
                   Array.Empty<SubSceneDestroyedIdentityHandler>(),
                   prefabMigrations)
        {
        }

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
            Func<Entity, PrefabIdentityInfo?> prefabIdentityProvider,
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
            PrefabIdentityProvider = prefabIdentityProvider;
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
            || PrefabIdentityProvider != null
            || DestroyedHandlers.Count > 0
            || PrefabMigrations.Count > 0;

        public bool HasTombstones
            => RegisterDestroyedEntities && DestroyedHandlers.Count > 0;

        public bool HasPrefabIdentity
            => PrefabIdentityProvider != null;
    }
}
