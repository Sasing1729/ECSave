using System;
using System.Collections.Generic;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Entities;

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
        private Func<Entity, PrefabIdentityInfo?> _prefabIdentityProvider;
        private readonly List<PrefabMigrationStepBase> _prefabMigrations = new();

        public ISubSceneDefinitionBuilder RegisterSceneLoadState() { _registerSceneLoadState = true; return this; }
        public ISubSceneDefinitionBuilder RegisterDestroyedEntities() { _registerDestroyedEntities = true; return this; }

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

        public ISubSceneDefinitionBuilder WithSceneStateRollback() { _withSceneStateRollback = true; return this; }

        public ISubSceneDefinitionBuilder RegisterPrefabMigration<TPrefabMigration>()
            where TPrefabMigration : class, IPrefabMigration => this;

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

        // === S2b-1 追加 ===
        public ISubSceneDefinitionBuilder WithPrefabIdentityProvider(
            Func<Entity, PrefabIdentityInfo?> provider)
        {
            _prefabIdentityProvider = provider;
            return this;
        }
        // === ここまで ===

        public void MarkSceneRestoreCallbackDeclared() { _hasSceneRestoreCallback = true; }

        public void RegisterPrefabMigrationStep(PrefabMigrationStepBase step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            // Review #13a: 同一 OldPrefabSchemaHash の重複登録をビルド時に検出する。
            // 旧実装は PrefabMigrationTable.From で throw しており、
            // 毎回のロード時（D5_5）にしか検出できなかった。
            for (int i = 0; i < _prefabMigrations.Count; i++)
            {
                var existing = _prefabMigrations[i];
                if (existing.GetType() == step.GetType()) return;
                if (existing.OldPrefabSchemaHash == step.OldPrefabSchemaHash)
                {
                    throw new SaveRegistrationException(
                        "ECSAVE-R030",
                        $"Prefab migration already registered for OldPrefabSchemaHash=0x{step.OldPrefabSchemaHash:X16}: " +
                        $"{existing.GetType().FullName} vs {step.GetType().FullName}");
                }
            }
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

            if (_withSceneStateRollback)
            {
                if (_sceneRestoreCallback == null)
                {
                    UnityEngine.Debug.LogWarning(
                        "[ECSave] WithSceneStateRollback is enabled but no SceneRestoreCallback is provided. " +
                        "Scene state cannot be rolled back on load failure.");
                }
                else if (_sceneStateProvider == null)
                {
                    UnityEngine.Debug.LogWarning(
                        "[ECSave] WithSceneStateRollback is enabled but no SceneStateProvider is provided. " +
                        "Rollback will use inverse of the original requests (best effort).");
                }
            }

            if (_contentVerification == ContentVerificationMode.GuidAndContentHash && _sceneStateProvider == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[ECSave] ContentVerificationMode.GuidAndContentHash requires SceneStateProvider " +
                    "that returns SceneLoadInfo.ContentHash. Verification will be skipped.");
            }

            // Review #13c: Migration を登録しても Provider が無いと機能しない（サイレント失敗）。
            if (_prefabMigrations.Count > 0 && _prefabIdentityProvider == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[ECSave] Prefab migrations are registered but no PrefabIdentityProvider is set. " +
                    "Migrations will never run. Call WithPrefabIdentityProvider(...) to enable prefab migration.");
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
                _prefabIdentityProvider,
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
