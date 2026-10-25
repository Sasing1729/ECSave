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
        private bool _disposed;
        private Exception _buildError;

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

            // binder の解決は GetType().FullName ベース。
            // Source Generator は type.ToDisplayString()（= ネスト型で '.' 区切り）で
            // ScopeId を emit するため、FullName（= ネスト型で '+' 区切り）を
            // '.' に正規化した形も試す。
            if (TryResolveMainBinder(out var mainBinder))
            {
                mainBinder.BindModules(mainBuilder);
            }

            if (TryResolveSubBinder(out var subBinder))
            {
                subBinder.BindSubScene(subSceneBuilder);
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

            return new SaveSystem(def);
        }

        /// <summary>
        /// 自身の型名で binder を解決する。
        ///
        /// <para>
        /// <b>正規化</b>: <c>GetType().FullName</c> はネスト型で <c>'+'</c> 区切り
        /// （<c>A.B+Inner</c>）だが、Source Generator は <c>ToDisplayString()</c> で
        /// <c>'.'</c> 区切り（<c>A.B.Inner</c>）の <c>ScopeId</c> を emit する。
        /// 両方を試すことで、あらゆる型階層で解決できる。
        /// </para>
        /// </summary>
        private bool TryResolveMainBinder(out ISaveModuleBinder binder)
        {
            binder = null;
            var rawId = GetType().FullName;
            if (string.IsNullOrEmpty(rawId)) return false;

            if (GeneratedScopeRegistry.TryGet(rawId, out binder))
                return true;

            var normalized = rawId.Replace('+', '.');
            return normalized != rawId && GeneratedScopeRegistry.TryGet(normalized, out binder);
        }

        private bool TryResolveSubBinder(out ISubSceneBinder binder)
        {
            binder = null;
            var rawId = GetType().FullName;
            if (string.IsNullOrEmpty(rawId)) return false;

            if (GeneratedScopeRegistry.TryGetSubScene(rawId, out binder))
                return true;

            var normalized = rawId.Replace('+', '.');
            return normalized != rawId && GeneratedScopeRegistry.TryGetSubScene(normalized, out binder);
        }

        protected abstract void Configure(ISaveDefinitionBuilder builder);

        protected virtual void ConfigureSubScene(ISubSceneDefinitionBuilder builder) { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _saveSystem = null;
            _buildError = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
