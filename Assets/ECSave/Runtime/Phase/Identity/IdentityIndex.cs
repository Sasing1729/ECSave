using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    public struct IdentityIndex : IDisposable
    {
        private NativeList<byte> _arena;
        private NativeList<IdentityEntry> _entries;
        private NativeParallelMultiHashMap<ulong, int> _buckets;
        private int _typeCount;

        public IdentityIndex(int initialCapacity, int typeCount, Allocator allocator)
        {
            _arena = new NativeList<byte>(initialCapacity * 16, allocator);
            _entries = new NativeList<IdentityEntry>(initialCapacity, allocator);
            _buckets = new NativeParallelMultiHashMap<ulong, int>(initialCapacity, allocator);
            _typeCount = typeCount;
        }

        public bool IsCreated => _arena.IsCreated;
        public int Count => _entries.Length;

        public unsafe int AddWorldEntry(uint typeIndex, byte* key, int length, Entity entity)
        {
            EnsureTypeIndex(typeIndex);
            int arenaOffset = AppendKey(key, length);
            int entryIndex = _entries.Length;
            _entries.Add(new IdentityEntry
            {
                TypeIndex = typeIndex,
                ArenaOffset = arenaOffset,
                Length = length,
                WorldEntity = entity,
                SavePersistentId = -1,
            });
            _buckets.Add(HashKey(typeIndex, key, length), entryIndex);
            return entryIndex;
        }

        public unsafe int AddSaveEntry(uint typeIndex, byte* key, int length, int persistentId)
        {
            EnsureTypeIndex(typeIndex);
            int arenaOffset = AppendKey(key, length);
            int entryIndex = _entries.Length;
            _entries.Add(new IdentityEntry
            {
                TypeIndex = typeIndex,
                ArenaOffset = arenaOffset,
                Length = length,
                WorldEntity = Entity.Null,
                SavePersistentId = persistentId,
            });
            _buckets.Add(HashKey(typeIndex, key, length), entryIndex);
            return entryIndex;
        }

        public unsafe bool TryMatchSaveEntry(uint typeIndex, byte* key, int length, out int saveEntryIndex)
        {
            saveEntryIndex = -1;
            ulong hash = HashKey(typeIndex, key, length);

            if (!_buckets.TryGetFirstValue(hash, out int entryIndex, out var it))
                return false;

            do
            {
                ref var e = ref _entries.ElementAt(entryIndex);
                if (e.TypeIndex == typeIndex && !e.IsWorldSide && KeyEquals(in e, key, length))
                {
                    saveEntryIndex = entryIndex;
                    return true;
                }
            } while (_buckets.TryGetNextValue(out entryIndex, ref it));

            return false;
        }

        public unsafe bool TryMatchWorldEntity(uint typeIndex, byte* key, int length, out Entity worldEntity)
        {
            worldEntity = Entity.Null;
            ulong hash = HashKey(typeIndex, key, length);

            if (!_buckets.TryGetFirstValue(hash, out int entryIndex, out var it))
                return false;

            do
            {
                ref var e = ref _entries.ElementAt(entryIndex);
                if (e.TypeIndex == typeIndex && e.IsWorldSide && KeyEquals(in e, key, length))
                {
                    worldEntity = e.WorldEntity;
                    return true;
                }
            } while (_buckets.TryGetNextValue(out entryIndex, ref it));

            return false;
        }

        public unsafe int CountWorldMatches(uint typeIndex, byte* key, int length)
        {
            ulong hash = HashKey(typeIndex, key, length);
            int n = 0;

            if (!_buckets.TryGetFirstValue(hash, out int entryIndex, out var it))
                return 0;

            do
            {
                ref var e = ref _entries.ElementAt(entryIndex);
                if (e.TypeIndex == typeIndex && e.IsWorldSide && KeyEquals(in e, key, length))
                    n++;
            } while (_buckets.TryGetNextValue(out entryIndex, ref it));

            return n;
        }

        public unsafe int CountSaveMatches(uint typeIndex, byte* key, int length)
        {
            ulong hash = HashKey(typeIndex, key, length);
            int n = 0;

            if (!_buckets.TryGetFirstValue(hash, out int entryIndex, out var it))
                return 0;

            do
            {
                ref var e = ref _entries.ElementAt(entryIndex);
                if (e.TypeIndex == typeIndex && !e.IsWorldSide && KeyEquals(in e, key, length))
                    n++;
            } while (_buckets.TryGetNextValue(out entryIndex, ref it));

            return n;
        }

        public unsafe bool HasWorldConflict(uint typeIndex, byte* key, int length)
            => CountWorldMatches(typeIndex, key, length) >= 1;

        public unsafe bool HasSaveConflict(uint typeIndex, byte* key, int length)
            => CountSaveMatches(typeIndex, key, length) >= 1;

        private unsafe int AppendKey(byte* key, int length)
        {
            int offset = _arena.Length;
            if (length > 0)
            {
                _arena.ResizeUninitialized(_arena.Length + length);
                var dst = (byte*)_arena.GetUnsafePtr() + offset;
                for (int i = 0; i < length; i++) dst[i] = key[i];
            }
            return offset;
        }

        private unsafe ulong HashKey(uint typeIndex, byte* key, int length)
        {
            ulong h = Format.SchemaHashCore.Hash(key, length);
            return h ^ ((ulong)typeIndex * 0x9E3779B97F4A7C15UL);
        }

        private unsafe bool KeyEquals(in IdentityEntry e, byte* key, int length)
        {
            if (e.Length != length) return false;
            byte* arena = (byte*)_arena.GetUnsafeReadOnlyPtr() + e.ArenaOffset;
            for (int i = 0; i < length; i++)
                if (arena[i] != key[i]) return false;
            return true;
        }

        private void EnsureTypeIndex(uint typeIndex)
        {
            if (_typeCount > 0 && typeIndex >= (uint)_typeCount)
                throw new ArgumentOutOfRangeException(nameof(typeIndex),
                    $"typeIndex {typeIndex} >= typeCount {_typeCount}");
        }

        public void Clear()
        {
            if (!IsCreated) return;
            _arena.Clear();
            _entries.Clear();
            _buckets.Clear();
        }

        public void Dispose()
        {
            if (_arena.IsCreated) _arena.Dispose();
            if (_entries.IsCreated) _entries.Dispose();
            if (_buckets.IsCreated) _buckets.Dispose();
            _arena = default;
            _entries = default;
            _buckets = default;
        }
    }
}
