using System;
using System.Collections.Generic;
using ECSave.Internal.Contract;
using UnityEngine;

namespace ECSave
{
    public abstract class SaveScope : ScriptableObject, IDisposable
    {
        private SaveSystem _saveSystem;
        private readonly List<string> _warnings = new List<string>();
        private bool _disposed;

        public IReadOnlyList<string> RegistrationWarnings => _warnings;

        public SaveSystem GetSaveSystem()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SaveScope));
            if (_saveSystem != null) return _saveSystem;

            var mainBuilder = new SaveDefinitionBuilderImpl();
            Configure(mainBuilder);

            var subSceneBuilder = new SubSceneDefinitionBuilderImpl();
            ConfigureSubScene(subSceneBuilder);

            string rawId = GetScopeId();
            string normalizedId = rawId != null ? rawId.Replace('+', '.') : rawId;

            ISaveModuleBinder mainBinder = null;
            bool mainFound = false;
            if (rawId != null && GeneratedScopeRegistry.TryGet(rawId, out mainBinder))
                mainFound = true;
            else if (normalizedId != null && normalizedId != rawId
                     && GeneratedScopeRegistry.TryGet(normalizedId, out mainBinder))
                mainFound = true;

            if (mainFound && mainBinder != null)
                mainBinder.BindModules(mainBuilder);

            ISubSceneBinder subBinder = null;
            bool subFound = false;
            if (rawId != null && GeneratedScopeRegistry.TryGetSubScene(rawId, out subBinder))
                subFound = true;
            else if (normalizedId != null && normalizedId != rawId
                     && GeneratedScopeRegistry.TryGetSubScene(normalizedId, out subBinder))
                subFound = true;

            if (subFound && subBinder != null)
                subBinder.BindSubScene(subSceneBuilder);

            var def = mainBuilder.Build();
            def.SubScene = subSceneBuilder.Build();

            _warnings.Clear();
            _warnings.AddRange(mainBuilder.Warnings);

            _saveSystem = new SaveSystem(def);
            return _saveSystem;
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
            _warnings.Clear();
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
