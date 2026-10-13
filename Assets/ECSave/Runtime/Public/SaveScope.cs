using System;
using System.Collections.Generic;
using ECSave.Internal.Contract;
using UnityEngine;

namespace ECSave
{
    public abstract class SaveScope : ScriptableObject, IDisposable
    {
        private SaveSystem _system;
        private List<string> _warnings;
        private bool _disposed;

        public IReadOnlyList<string> RegistrationWarnings
            => _warnings ?? (IReadOnlyList<string>)Array.Empty<string>();

        public SaveSystem GetSaveSystem()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SaveScope));
            if (_system != null) return _system;

            var builder = new SaveDefinitionBuilderImpl();

            Configure(builder);

            var scopeId = GetType().FullName;
            if (scopeId != null && GeneratedScopeRegistry.TryGet(scopeId, out var binder))
            {
                binder.BindModules(builder);
            }

            var definition = builder.Build();
            _warnings = new List<string>(builder.Warnings);
            _system = new SaveSystem(definition);
            return _system;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _system = null;
            _warnings = null;
        }

        protected abstract void Configure(ISaveDefinitionBuilder builder);

        private void OnDestroy() => Dispose();
    }
}
