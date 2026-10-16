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
        private readonly Dictionary<string, string> _renamedFrom = new();
        private readonly List<string> _warnings = new();

        private int _gameDataVersion = 1;
        private string _worldId = "";

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
            if (new TMigration() is MigrationStepBase step)
                _migrations.Add(step);
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

        public void RegisterModule(SaveTypeModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_modules.Contains(module))
                throw new SaveRegistrationException("ECSAVE-R001",
                    $"Module already registered: {module.FullTypeName}");
            _modules.Add(module);
        }

        public void RegisterIdentityModule(IdentityModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_identities.Contains(module))
                throw new SaveRegistrationException("ECSAVE-R001",
                    $"Identity module already registered: {module.FullTypeName}");
            _identities.Add(module);
        }

        public void RegisterSelectionModule(SelectionModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_selections.Contains(module))
                throw new SaveRegistrationException("ECSAVE-R001",
                    $"Selection module already registered: {module.FullTypeName}.{module.FieldPath}");
            _selections.Add(module);
        }

        public void RegisterMigrationStep(MigrationStepBase step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            _migrations.Add(step);
        }

        internal void RecordWithoutEnableable(Type t) { }
        internal void RecordWithAll(Type t, Type filter) { }
        internal void RecordWithAny(Type t, Type filter) { }
        internal void RecordWithNone(Type t, Type filter) { }

        internal void RecordRenamedFrom(Type t, string oldName)
        {
            string newName = t.FullName.Replace('+', '.');

            if (oldName == newName)
                throw new SaveRegistrationException("ECSAVE-R006",
                    $"RenamedFrom must differ from the current name: {newName}");

            if (_renamedFrom.TryGetValue(oldName, out var existing))
            {
                if (existing == newName) return;
                throw new SaveRegistrationException("ECSAVE-R007",
                    $"RenamedFrom '{oldName}' is already mapped to '{existing}'");
            }

            _renamedFrom.Add(oldName, newName);
        }

        internal SaveDefinition Build()
        {
            var def = new SaveDefinition(
                _modules,
                _identities,
                _selections,
                _gameDataVersion,
                _worldId);

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
    }
}
