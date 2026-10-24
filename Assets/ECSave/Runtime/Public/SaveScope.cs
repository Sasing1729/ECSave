using System;
using System.Collections.Generic;
using ECSave.Internal.Contract;
using ECSave.Internal.SubScene;
using UnityEngine;

namespace ECSave
{
    public abstract class SaveScope : ScriptableObject, IDisposable
    {
        private SaveSystem _saveSystem;
        private readonly List<string> _warnings = new List<string>();
        private bool _disposed;
        private Exception _buildError;

        public IReadOnlyList<string> RegistrationWarnings => _warnings;

        public SaveSystem GetSaveSystem()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SaveScope));

            if (_buildError != null)
            {
                throw new InvalidOperationException(
                    $"GetSaveSystem() previously failed with {_buildError.GetType().Name}: " +
                    _buildError.Message +
                    " Fix the underlying Configure/ConfigureSubScene issue and recreate the SaveScope instance. " +
                    "This SaveScope will not retry building because the failure is considered permanent.",
                    _buildError);
            }

            if (_saveSystem != null) return _saveSystem;

            try
            {
                _saveSystem = BuildSaveSystem();
                return _saveSystem;
            }
            catch (Exception ex)
            {
                _buildError = ex;
                throw;
            }
        }

        private SaveSystem BuildSaveSystem()
        {
            var mainBuilder = new SaveDefinitionBuilderImpl();
            Configure(mainBuilder);

            var subSceneBuilder = new SubSceneDefinitionBuilderImpl();
            ConfigureSubScene(subSceneBuilder);

            string rawCustomId = GetScopeId();
            string normalizedCustomId = rawCustomId != null ? rawCustomId.Replace('+', '.') : null;
            string rawTypeId = GetType().FullName;
            string normalizedTypeId = rawTypeId != null ? rawTypeId.Replace('+', '.') : null;

            if (TryResolveMainBinder(rawCustomId, normalizedCustomId, out var mainBinder)
                || TryResolveMainBinder(rawTypeId, normalizedTypeId, out mainBinder))
            {
                mainBinder?.BindModules(mainBuilder);
            }

            if (TryResolveSubBinder(rawCustomId, normalizedCustomId, out var subBinder)
                || TryResolveSubBinder(rawTypeId, normalizedTypeId, out subBinder))
            {
                subBinder?.BindSubScene(subSceneBuilder);
            }

            var def = mainBuilder.Build();
            var sub = subSceneBuilder.Build();

            if (sub.RegisterDestroyedEntities && sub.DestroyedHandlers.Count > 0)
            {
                var registeredNames = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < def.Identities.Count; i++)
                {
                    var im = def.Identities[i];
                    if (im != null && !string.IsNullOrEmpty(im.FullTypeName))
                        registeredNames.Add(im.FullTypeName);
                }

                var filtered = new List<SubSceneDestroyedIdentityHandler>();
                for (int i = 0; i < sub.DestroyedHandlers.Count; i++)
                {
                    var h = sub.DestroyedHandlers[i];
                    if (h != null && registeredNames.Contains(h.FullTypeName))
                        filtered.Add(h);
                }

                if (filtered.Count != sub.DestroyedHandlers.Count)
                {
                    sub = new SubSceneDefinition(
                        sub.RegisterSceneLoadState,
                        sub.RegisterDestroyedEntities,
                        sub.HasSceneRestoreCallback,
                        sub.WithSceneStateRollback,
                        sub.ContentVerification,
                        sub.StreamingConsistency,
                        sub.TombstoneCapacity,
                        sub.SceneStateProvider,
                        sub.SceneRestoreCallback,
                        sub.PrefabIdentityProvider,
                        filtered,
                        sub.PrefabMigrations);
                }
            }

            def.SubScene = sub;

            _warnings.Clear();
            _warnings.AddRange(mainBuilder.Warnings);

            return new SaveSystem(def);
        }

        private static bool TryResolveMainBinder(
            string rawId, string normalizedId, out ISaveModuleBinder binder)
        {
            binder = null;
            if (!string.IsNullOrEmpty(rawId) && GeneratedScopeRegistry.TryGet(rawId, out binder))
                return true;
            if (!string.IsNullOrEmpty(normalizedId) && normalizedId != rawId
                && GeneratedScopeRegistry.TryGet(normalizedId, out binder))
                return true;
            return false;
        }

        private static bool TryResolveSubBinder(
            string rawId, string normalizedId, out ISubSceneBinder binder)
        {
            binder = null;
            if (!string.IsNullOrEmpty(rawId) && GeneratedScopeRegistry.TryGetSubScene(rawId, out binder))
                return true;
            if (!string.IsNullOrEmpty(normalizedId) && normalizedId != rawId
                && GeneratedScopeRegistry.TryGetSubScene(normalizedId, out binder))
                return true;
            return false;
        }

        protected abstract void Configure(ISaveDefinitionBuilder builder);

        protected virtual void ConfigureSubScene(ISubSceneDefinitionBuilder builder) { }

        protected virtual string GetScopeId()
        {
            return GetType().FullName;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _saveSystem = null;
            _buildError = null;
            _warnings.Clear();
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
