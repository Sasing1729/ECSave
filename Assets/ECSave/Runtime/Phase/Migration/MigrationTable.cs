using System.Collections.Generic;

namespace ECSave.Internal
{
    public readonly struct MigrationEntry
    {
        public ulong NewTypeNameHash { get; }
        public ulong OldSchemaHash { get; }
        public ulong NewSchemaHash { get; }
        public string NewTypeName { get; }

        public MigrationEntry(ulong a, ulong b, ulong c, string name)
        {
            NewTypeNameHash = a;
            OldSchemaHash = b;
            NewSchemaHash = c;
            NewTypeName = name;
        }
    }

    public readonly struct RenamedEntry
    {
        public ulong OldFullNameHash { get; }
        public ulong NewFullNameHash { get; }
        public string OldFullName { get; }
        public string NewFullName { get; }

        public RenamedEntry(ulong a, ulong b, string oldName, string newName)
        {
            OldFullNameHash = a;
            NewFullNameHash = b;
            OldFullName = oldName;
            NewFullName = newName;
        }
    }

    public sealed class MigrationTable
    {
        private readonly Dictionary<MigrationKey, MigrationStepBase> _byKey
            = new Dictionary<MigrationKey, MigrationStepBase>();

        private readonly Dictionary<ulong, ulong> _renamedTo
            = new Dictionary<ulong, ulong>();

        private readonly Dictionary<ulong, string> _renameOldName
            = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, string> _renameNewName
            = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, string> _typeNameByHash
            = new Dictionary<ulong, string>();

        public int Count => _byKey.Count;
        public int RenamedCount => _renamedTo.Count;

        public void Register(MigrationStepBase step)
        {
            if (step == null) throw new System.ArgumentNullException(nameof(step));

            var key = new MigrationKey(step.NewTypeNameHash, step.OldSchemaHash);

            if (_byKey.ContainsKey(key))
                throw new System.InvalidOperationException(
                    $"Migration already registered for New={key.NewTypeNameHash:X16}, Old={key.OldSchemaHash:X16}");

            _byKey.Add(key, step);
        }

        public void RegisterTypeName(ulong typeNameHash, string fullName)
        {
            if (fullName == null) return;
            _typeNameByHash[typeNameHash] = fullName;
        }

        public MigrationStepBase Resolve(ulong newTypeNameHash, ulong oldSchemaHash)
        {
            _byKey.TryGetValue(new MigrationKey(newTypeNameHash, oldSchemaHash), out var step);
            return step;
        }

        public void RegisterRenamed(ulong oldFullNameHash, ulong newFullNameHash)
            => RegisterRenamed(oldFullNameHash, newFullNameHash, null, null);

        public void RegisterRenamed(ulong oldFullNameHash, ulong newFullNameHash, string oldName, string newName)
        {
            if (_renamedTo.ContainsKey(oldFullNameHash))
                throw new System.InvalidOperationException(
                    $"RenamedFrom already registered for 0x{oldFullNameHash:X16}");

            _renamedTo.Add(oldFullNameHash, newFullNameHash);
            if (oldName != null) _renameOldName[oldFullNameHash] = oldName;
            if (newName != null) _renameNewName[oldFullNameHash] = newName;
        }

        public bool TryResolveRenamed(ulong oldFullNameHash, out ulong newFullNameHash)
            => _renamedTo.TryGetValue(oldFullNameHash, out newFullNameHash);

        public IEnumerable<MigrationEntry> EnumerateEntries()
        {
            foreach (var kv in _byKey)
            {
                string name;
                if (!_typeNameByHash.TryGetValue(kv.Key.NewTypeNameHash, out name))
                    name = null;

                yield return new MigrationEntry(
                    kv.Key.NewTypeNameHash,
                    kv.Key.OldSchemaHash,
                    kv.Value.NewSchemaHash,
                    name);
            }
        }

        public IEnumerable<RenamedEntry> EnumerateRenamed()
        {
            foreach (var kv in _renamedTo)
            {
                string oldName;
                string newName;
                _renameOldName.TryGetValue(kv.Key, out oldName);
                _renameNewName.TryGetValue(kv.Key, out newName);

                yield return new RenamedEntry(kv.Key, kv.Value, oldName, newName);
            }
        }
    }
}
