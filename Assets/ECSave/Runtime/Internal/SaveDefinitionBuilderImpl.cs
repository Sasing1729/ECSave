using System;
using System.Collections.Generic;
using ECSave.Format;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave
{
    public sealed class SaveDefinitionBuilderImpl : ISaveDefinitionBuilder
    {
        private readonly List<SaveTypeModule> _modules = new();
        private readonly List<IdentityModule> _identities = new();
        private readonly List<SelectionModule> _selections = new();
        private readonly List<MigrationStepBase> _migrations = new();
        private readonly List<PendingMigration> _pendingMigrations = new();
        private readonly Dictionary<string, string> _renamedFrom = new();
        private readonly List<string> _warnings = new();

        private int _gameDataVersion = 1;
        private string _worldId = "";
        private bool _autoRepairFixedString = true;

        public IReadOnlyList<string> Warnings => _warnings;

        public SaveDefinitionBuilder<T> RegisterComponent<T>() where T : unmanaged, IComponentData
            => new(this);

        public SaveDefinitionBuilder<T> RegisterBuffer<T>() where T : unmanaged, IBufferElementData
            => new(this);

        public SaveDefinitionBuilder<T> RegisterSharedComponent<T>() where T : unmanaged, ISharedComponentData
            => new(this);

        public SaveDefinitionBuilder<T> RegisterChunkComponent<T>() where T : unmanaged, IComponentData
            => new(this);

        public void RegisterIdentity<T>() where T : unmanaged, IComponentData { }

        public void RegisterMigration<TMigration>() where TMigration : class, new()
        {
            var type = typeof(TMigration);

            if (typeof(MigrationStepBase).IsAssignableFrom(type))
            {
                _migrations.Add((MigrationStepBase)Activator.CreateInstance(type));
                return;
            }

            var newType = ExtractNewTypeArgument(type);
            if (newType != null)
            {
                for (int i = 0; i < _pendingMigrations.Count; i++)
                {
                    if (_pendingMigrations[i].MigrationType == type)
                        return;
                }

                var newTypeName = (newType.FullName ?? newType.Name).Replace('+', '.');
                _pendingMigrations.Add(new PendingMigration
                {
                    MigrationType = type,
                    NewTypeName = newTypeName,
                });
                return;
            }

            throw new SaveRegistrationException(
                "ECSAVE-R008",
                $"RegisterMigration<{type.FullName}> must derive from MigrationStepBase or implement IComponentMigration<TOld, TNew>.");
        }

        public void SelectEntityReference<T>(Func<T, Entity> field) where T : unmanaged, IComponentData { }

        public void SelectEntityReferences<T>(Func<T, Entity> field) where T : unmanaged, IBufferElementData { }

        public void GameDataVersion(int version)
        {
            if (version < 1)
                throw new SaveRegistrationException("ECSAVE-R002", "GameDataVersion must be >= 1");
            _gameDataVersion = version;
        }

        public void WorldId(string id)
        {
            _worldId = id ?? string.Empty;
        }

        public void DisableAutoRepairFixedString()
        {
            _autoRepairFixedString = false;
        }

        public void RegisterModule(SaveTypeModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_modules.Contains(module))
                throw new SaveRegistrationException("ECSAVE-R001",
                    $"Module already registered: {module.FullTypeName}");

            module.RegistrationId = _modules.Count;
            _modules.Add(module);
        }

        public void RegisterIdentityModule(IdentityModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_identities.Contains(module))
                throw new SaveRegistrationException("ECSAVE-R001",
                    $"Identity module already registered: {module.FullTypeName}");

            module.RegistrationId = _identities.Count;
            _identities.Add(module);
        }

        public void RegisterSelectionModule(SelectionModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_selections.Contains(module))
                throw new SaveRegistrationException("ECSAVE-R001",
                    $"Selection module already registered: {module.FullTypeName}.{module.FieldPath}");

            module.RegistrationId = _selections.Count;
            _selections.Add(module);
        }

        public void RegisterMigrationStep(MigrationStepBase step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            _migrations.Add(step);

            var stepName = NormalizeTypeName(step.NewTypeName);
            if (stepName == null) return;

            for (int i = _pendingMigrations.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_pendingMigrations[i].NewTypeName, stepName, StringComparison.Ordinal))
                    _pendingMigrations.RemoveAt(i);
            }
        }

        internal void RecordWithoutEnableable(Type t) { }
        internal void RecordWithAll(Type t, Type filter) { }
        internal void RecordWithAny(Type t, Type filter) { }
        internal void RecordWithNone(Type t, Type filter) { }

        internal void RecordRenamedFrom(Type t, string oldName)
        {
            if (string.IsNullOrEmpty(oldName))
                throw new SaveRegistrationException("ECSAVE-R005",
                    "RenamedFrom requires a non-empty old name");

            // oldName は呼び出し側が与える文字列であり、"A.B+Inner"（リフレクション形式）と
            // "A.B.Inner"（Roslyn 形式）の双方が渡され得る。Source Generator が
            // ToDisplayString() で '.' 形式の NameHash を出力するため、比較・保存の
            // 両方で '.' 形式に正規化する。
            string normalizedOldName = oldName.Replace('+', '.');
            string newName = (t.FullName ?? t.Name).Replace('+', '.');

            if (normalizedOldName == newName)
                throw new SaveRegistrationException("ECSAVE-R006",
                    $"RenamedFrom must differ from the current name: {newName}");

            if (_renamedFrom.TryGetValue(normalizedOldName, out var existing))
            {
                if (existing == newName) return;
                throw new SaveRegistrationException("ECSAVE-R007",
                    $"RenamedFrom '{normalizedOldName}' is already mapped to '{existing}'");
            }

            _renamedFrom.Add(normalizedOldName, newName);
        }

        internal SaveDefinition Build()
        {
            if (_pendingMigrations.Count > 0)
            {
                var names = new List<string>(_pendingMigrations.Count);
                for (int i = 0; i < _pendingMigrations.Count; i++)
                    names.Add(_pendingMigrations[i].MigrationType.FullName);
                throw new SaveRegistrationException(
                    "ECSAVE-R011",
                    "RegisterMigration<T>() was called but no corresponding MigrationStepBase was registered. " +
                    "This usually indicates the Source Generator did not process this scope, or the generated binder was not invoked. " +
                    "Migrated types: " + string.Join(", ", names));
            }

            var def = new SaveDefinition(
                _modules,
                _identities,
                _selections,
                _gameDataVersion,
                _worldId)
            {
                AutoRepairFixedString = _autoRepairFixedString,
            };

            if (_migrations.Count > 0 || _renamedFrom.Count > 0)
            {
                var table = new MigrationTable();
                foreach (var step in _migrations) table.Register(step);
                foreach (var kv in _renamedFrom)
                    table.RegisterRenamed(
                        SchemaHashCore.HashUtf8(kv.Key),
                        SchemaHashCore.HashUtf8(kv.Value),
                        kv.Key,
                        kv.Value);
                def.Migrations = table;
            }
            return def;
        }

        private static Type ExtractNewTypeArgument(Type type)
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType) continue;
                if (iface.GetGenericTypeDefinition() != typeof(IComponentMigration<,>)) continue;
                return iface.GetGenericArguments()[1];
            }
            return null;
        }

        private static string NormalizeTypeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("global::", StringComparison.Ordinal))
                name = name.Substring(8);
            return name.Replace('+', '.');
        }

        private sealed class PendingMigration
        {
            public Type MigrationType;
            public string NewTypeName;
        }
    }
}
