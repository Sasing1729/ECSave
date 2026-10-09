using System;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// 型登録・識別型・選択ごとの EntityQuery を World 単位で保持する（10.1節）。
    /// メインスレッドのみで触る。Allocator は Persistent（World 存続中）。
    /// </summary>
    public struct QueryCache : IDisposable
    {
        private NativeParallelHashMap<int, EntityQuery> _queries;
        private bool _created;

        public QueryCache(int initialCapacity, Allocator allocator)
        {
            _queries = new NativeParallelHashMap<int, EntityQuery>(initialCapacity, allocator);
            _created = true;
        }

        public bool IsCreated => _created && _queries.IsCreated;
        public int Count => _created ? _queries.Count() : 0;

        /// <summary>
        /// 登録 ID に対する EntityQuery を返す。無ければ factory で作る。
        /// 新規作成した場合 true、既存を返した場合 false。
        /// </summary>
        public bool TryGetOrAdd(int registrationId, Func<EntityQuery> factory, out EntityQuery query)
        {
            ThrowIfNotCreated();
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            if (_queries.TryGetValue(registrationId, out query))
                return false;

            query = factory();
            _queries.Add(registrationId, query);
            return true;
        }

        public bool TryGet(int registrationId, out EntityQuery query)
        {
            ThrowIfNotCreated();
            return _queries.TryGetValue(registrationId, out query);
        }

        public void Clear()
        {
            if (_queries.IsCreated) _queries.Clear();
        }

        private void ThrowIfNotCreated()
        {
            if (!IsCreated)
                throw new ObjectDisposedException(nameof(QueryCache));
        }

        /// <summary>
        /// HashMap を破棄する。EntityQuery は SystemState 所有のため、ここでは破棄しない。
        /// </summary>
        public void Dispose()
        {
            if (_queries.IsCreated)
            {
                _queries.Dispose();
                _queries = default;
            }
            _created = false;
        }
    }
}
