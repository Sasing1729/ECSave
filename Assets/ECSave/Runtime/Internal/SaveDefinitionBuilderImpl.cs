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

        private int _gameDataVersion = 1;
        private string _worldId = "";
        private bool _autoRepairFixedString = true;

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

            if (ExtractNewTypeArgument(type) != null)
                return;

            throw new SaveRegistrationException(
                RegistrationErrorCodes.MigrationTypeInvalid,
                $"RegisterMigration<{type.FullName}> must derive from MigrationStepBase " +
                $"or implement IComponentMigration<TOld, TNew>. " +
                $"If you intended to use IComponentMigration, verify that the Source Generator " +
                $"processed this scope (check for generator diagnostics).");
        }

        /// <summary>
        /// PidForm の登録（Phase 1: no-op）。
        ///
        /// <para>
        /// このメソッドは実行時には何もしない。Source Generator が
        /// <c>RegisterPidForm&lt;TSave, TPid&gt;()</c> 呼び出しを構文解析で検出し、
        /// 対応する <c>IPidMigration</c> 実装型の MigrationStep を生成する。
        /// 生成された Step は、生成された <c>BindModules</c> が
        /// <see cref="RegisterMigrationStep"/> 経由で登録する。
        /// </para>
        /// </summary>
        public void RegisterPidForm<TSave, TPid>()
            where TSave : unmanaged
            where TPid : unmanaged
        {
            // No-op. SG がコンパイル時に処理する。
        }

        /// <summary>
        /// PidMigration の登録（Phase 1: no-op）。
        ///
        /// <para>
        /// <see cref="RegisterPidForm{TSave, TPid}"/> と同様、実行時には何もしない。
        /// SG が <c>RegisterPidMigration&lt;TMigration&gt;()</c> 呼び出しを検出し、
        /// 対応する MigrationStep を生成する。
        /// </para>
        /// </summary>
        public void RegisterPidMigration<TMigration>() where TMigration : class, new()
        {
            // No-op. SG がコンパイル時に処理する。
        }

        public void SelectEntityReference<T>(Func<T, Entity> field) where T : unmanaged, IComponentData { }

        public void SelectEntityReferences<T>(Func<T, Entity> field) where T : unmanaged, IBufferElementData { }

        public void GameDataVersion(int version)
        {
            if (version < 1)
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.InvalidGameDataVersion,
                    "GameDataVersion must be >= 1");
            _gameDataVersion = version;
        }

        public void WorldId(string id)
        {
            if (id != null)
            {
                int byteCount = System.Text.Encoding.UTF8.GetByteCount(id);
                if (byteCount > ECSaveFormatConstants.MaxWorldIdLength)
                {
                    throw new SaveRegistrationException(
                        RegistrationErrorCodes.WorldIdTooLong,
                        $"WorldId exceeds {ECSaveFormatConstants.MaxWorldIdLength} UTF-8 bytes: " +
                        $"{byteCount} bytes (value='{id}')");
                }
            }
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
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.ModuleAlreadyRegistered,
                    $"Module already registered: {module.FullTypeName}");

            module.RegistrationId = _modules.Count;
            _modules.Add(module);
        }

        public void RegisterIdentityModule(IdentityModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_identities.Contains(module))
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.ModuleAlreadyRegistered,
                    $"Identity module already registered: {module.FullTypeName}");

            module.RegistrationId = _identities.Count;
            _identities.Add(module);
        }

        public void RegisterSelectionModule(SelectionModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_selections.Contains(module))
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.ModuleAlreadyRegistered,
                    $"Selection module already registered: {module.FullTypeName}.{module.FieldPath}");

            module.RegistrationId = _selections.Count;
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
            if (string.IsNullOrEmpty(oldName))
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.RenamedFromEmpty,
                    "RenamedFrom requires a non-empty old name");

            string normalizedOldName = oldName.Replace('+', '.');
            string newName = (t.FullName ?? t.Name).Replace('+', '.');

            if (normalizedOldName == newName)
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.RenamedFromSameAsCurrent,
                    $"RenamedFrom must differ from the current name: {newName}");

            if (_renamedFrom.TryGetValue(normalizedOldName, out var existing))
            {
                if (existing == newName) return;
                throw new SaveRegistrationException(
                    RegistrationErrorCodes.RenamedFromConflict,
                    $"RenamedFrom '{normalizedOldName}' is already mapped to '{existing}'");
            }

            _renamedFrom.Add(normalizedOldName, newName);
        }

        internal SaveDefinition Build()
        {
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
    }
}
