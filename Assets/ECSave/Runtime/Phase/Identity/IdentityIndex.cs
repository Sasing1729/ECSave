// Runtime/Phase/Identity/IdentityIndex.cs
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// 識別キーの照合・重複検出（9章）。
    /// 正準キーはアリーナに格納し、`NativeParallelMultiHashMap<ulong, int>` で索引する（9.1節）。
    /// A-18 の実測：100k で add 1.9 ms / lookup 2.1 ms、Burst 互換。
    /// </summary>
    public struct IdentityIndex : IDisposable
    {
        private NativeList<byte> _arena;
        private NativeList<IdentityEntry> _entries;
        private NativeParallelMultiHashMap<ulong, int> _buckets;

        /// <summary>識別型の数（衝突判定に使う）。</summary>
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

        /// <summary>World 側のエントリを追加する（9.2節）。</summary>
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

        /// <summary>SaveData 側のエントリを追加する（9.3節）。</summary>
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

        /// <summary>
        /// World 側のキーに一致する SaveData 側エントリを 1 つ返す（9.4節）。
        /// 一致しなければ false。
        /// </summary>
        public unsafe bool TryMatchSaveEntry(uint typeIndex, byte* key, int length, out int saveEntryIndex)
        {
            saveEntryIndex = -1;
            ulong hash = HashKey(typeIndex, key, length);

            if (!_buckets.TryGetFirstValue(hash, out int entryIndex, out var it))
                return false;

            do
            {
                ref var e = ref _entries.ElementAt(entryIndex);
                if (e.TypeIndex == typeIndex && KeyEquals(in e, key, length))
                {
                    // SaveData 側エントリのみを返す
                    if (!e.IsWorldSide)
                    {
                        saveEntryIndex = entryIndex;
                        return true;
                    }
                }
            } while (_buckets.TryGetNextValue(out entryIndex, ref it));

            return false;
        }

        /// <summary>
        /// World 側エントリの重複を検出する（9.2節）。
        /// 一致する別エントリが既にあれば true。
        /// </summary>
        public unsafe bool HasWorldConflict(uint typeIndex, byte* key, int length)
        {
            ulong hash = HashKey(typeIndex, key, length);
            if (!_buckets.TryGetFirstValue(hash, out int entryIndex, out var it))
                return false;

            do
            {
                ref var e = ref _entries.ElementAt(entryIndex);
                if (e.IsWorldSide && e.TypeIndex == typeIndex && KeyEquals(in e, key, length))
                    return true;
            } while (_buckets.TryGetNextValue(out entryIndex, ref it));

            return false;
        }

        /// <summary>SaveData 側エントリの重複を検出する（9.3節）。</summary>
        public unsafe bool HasSaveConflict(uint typeIndex, byte* key, int length)
        {
            ulong hash = HashKey(typeIndex, key, length);
            if (!_buckets.TryGetFirstValue(hash, out int entryIndex, out var it))
                return false;

            do
            {
                ref var e = ref _entries.ElementAt(entryIndex);
                if (!e.IsWorldSide && e.TypeIndex == typeIndex && KeyEquals(in e, key, length))
                    return true;
            } while (_buckets.TryGetNextValue(out entryIndex, ref it));

            return false;
        }

        /// <summary>
        /// 矛盾2 の検出用に、World Entity → SaveData エントリの逆引きを作る（9.4節）。
        /// </summary>
        public NativeParallelHashMap<Entity, int> BuildReverseMap(Allocator allocator)
        {
            var map = new NativeParallelHashMap<Entity, int>(_entries.Length, allocator);
            for (int i = 0; i < _entries.Length; i++)
            {
                ref var e = ref _entries.ElementAt(i);
                if (e.IsWorldSide) continue;
                // 逆引きは World 側の情報と突き合わせて構築する（Phase 6 で完成）
                // ここでは骨格のみ
                _ = map;
            }
            return map;
        }

        // ── 内部 ─────────────────────────────────

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
