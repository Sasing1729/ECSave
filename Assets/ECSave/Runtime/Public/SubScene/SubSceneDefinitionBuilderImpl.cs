using System;
using System.Collections.Generic;
using ECSave.Internal;
using ECSave.Internal.SubScene;

namespace ECSave
{
    public sealed class SubSceneDefinitionBuilderImpl : ISubSceneDefinitionBuilder
    {
        private bool _registerSceneLoadState;
        private bool _registerDestroyedEntities;
        private bool _hasSceneRestoreCallback;
        private bool _withSceneStateRollback;
        private ContentVerificationMode _contentVerification = ContentVerificationMode.None;
        private StreamingConsistencyMode _streamingConsistency = StreamingConsistencyMode.TimeBased;
        private int _tombstoneCapacity;
        private bool _contentVerificationSet;
        private bool _streamingConsistencySet;
        private bool _tombstoneCapacitySet;
        private Func<IReadOnlyList<SceneLoadInfo>> _sceneStateProvider;
        private Func<SceneLoadRequest[], SceneLoadOutcome[]> _sceneRestoreCallback;
        private readonly List<PrefabMigrationStepBase> _prefabMigrations = new();

        public ISubSceneDefinitionBuilder RegisterSceneLoadState()
        {
            _registerSceneLoadState = true;
            return this;
        }

        public ISubSceneDefinitionBuilder RegisterDestroyedEntities()
        {
            _registerDestroyedEntities = true;
            return this;
        }

        public ISubSceneDefinitionBuilder WithTombstoneCapacity(int capacity)
        {
            if (capacity < 0)
                throw new SaveRegistrationException(SubSceneRegistrationErrorCodes.NegativeTombstoneCapacity,
                    $"TombstoneCapacity must be >= 0, but was {capacity}");
            if (_tombstoneCapacitySet && _tombstoneCapacity != capacity)
                throw new SaveRegistrationException(SubSceneRegistrationErrorCodes.ConflictingSetting,
                    $"Conflicting WithTombstoneCapacity: {_tombstoneCapacity} vs {capacity}");
            _tombstoneCapacitySet = true;
            _tombstoneCapacity = capacity;
            return this;
        }

        public ISubSceneDefinitionBuilder WithContentVerification(ContentVerificationMode mode)
        {
            if (!IsValidContentVerificationMode(mode))
                throw new SaveRegistrationException(SubSceneRegistrationErrorCodes.InvalidEnumValue,
                    $"Invalid ContentVerificationMode: {(int)mode}");
            if (_contentVerificationSet && _contentVerification != mode)
                throw new SaveRegistrationException(SubSceneRegistrationErrorCodes.ConflictingSetting,
                    $"Conflicting WithContentVerification: {_contentVerification} vs {mode}");
            _contentVerificationSet = true;
            _contentVerification = mode;
            return this;
        }

        public ISubSceneDefinitionBuilder WithStreamingConsistency(StreamingConsistencyMode mode)
        {
            if (!IsValidStreamingConsistencyMode(mode))
                throw new SaveRegistrationException(SubSceneRegistrationErrorCodes.InvalidEnumValue,
                    $"Invalid StreamingConsistencyMode: {(int)mode}");
            if (_streamingConsistencySet && _streamingConsistency != mode)
                throw new SaveRegistrationException(SubSceneRegistrationErrorCodes.ConflictingSetting,
                    $"Conflicting WithStreamingConsistency: {_streamingConsistency} vs {mode}");
            _streamingConsistencySet = true;
            _streamingConsistency = mode;
            return this;
        }

        public ISubSceneDefinitionBuilder WithSceneStateRollback()
        {
            _withSceneStateRollback = true;
            return this;
        }

        public ISubSceneDefinitionBuilder RegisterPrefabMigration<TPrefabMigration>()
            where TPrefabMigration : class, IPrefabMigration
        {
            return this;
        }

        public ISubSceneDefinitionBuilder WithSceneStateProvider(Func<IReadOnlyList<SceneLoadInfo>> provider)
        {
            _sceneStateProvider = provider;
            return this;
        }

        public ISubSceneDefinitionBuilder WithSceneRestoreCallback(Func<SceneLoadRequest[], SceneLoadOutcome[]> restore)
        {
            _sceneRestoreCallback = restore;
            _hasSceneRestoreCallback = true;
            return this;
        }

        public void MarkSceneRestoreCallbackDeclared()
        {
            _hasSceneRestoreCallback = true;
        }

        public void RegisterPrefabMigrationStep(PrefabMigrationStepBase step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            for (int i = 0; i < _prefabMigrations.Count; i++)
                if (_prefabMigrations[i].GetType() == step.GetType()) return;
            _prefabMigrations.Add(step);
        }

        internal SubSceneDefinition Build()
        {
            if (_registerSceneLoadState && _sceneStateProvider == null && _sceneRestoreCallback == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[ECSave] RegisterSceneLoadState is set but neither SceneStateProvider nor SceneRestoreCallback is provided. " +
                    "SubScene state will not be tracked or restored.");
            }

            IReadOnlyList<SubSceneDestroyedIdentityHandler> handlers =
                _registerDestroyedEntities
                    ? SubSceneTombstone.GetRegisteredHandlers()
                    : Array.Empty<SubSceneDestroyedIdentityHandler>();

            if (_registerDestroyedEntities && handlers.Count == 0)
            {
                UnityEngine.Debug.LogWarning(
                    "[ECSave] RegisterDestroyedEntities is set but no identity type has been marked. " +
                    "Call SubSceneTombstone.MarkDestroyed<TIdentity>(em, id) before destroying entities.");
            }

            return new SubSceneDefinition(
                _registerSceneLoadState,
                _registerDestroyedEntities,
                _hasSceneRestoreCallback,
                _withSceneStateRollback,
                _contentVerification,
                _streamingConsistency,
                _tombstoneCapacity,
                _sceneStateProvider,
                _sceneRestoreCallback,
                handlers,
                _prefabMigrations);
        }

        private static bool IsValidContentVerificationMode(ContentVerificationMode m)
            => m == ContentVerificationMode.None
            || m == ContentVerificationMode.GuidOnly
            || m == ContentVerificationMode.GuidAndContentHash;

        private static bool IsValidStreamingConsistencyMode(StreamingConsistencyMode m)
            => m == StreamingConsistencyMode.TimeBased || m == StreamingConsistencyMode.FrameBoundary;
    }
}
