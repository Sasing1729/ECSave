using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// モジュールの <c>BuildQuery</c> 結果を World 単位でキャッシュする。
    ///
    /// <para>
    /// モジュールは Immutable なので、同じ (World, Module) のクエリは再利用できる。
    /// キャッシュにより <c>EntityManager.CreateEntityQuery</c> の呼び出し回数を削減する。
    /// </para>
    ///
    /// <para>
    /// <b>保持期間</b>: <see cref="ConditionalWeakTable{TKey, TValue}"/> により、
    /// World への参照は弱参照。World が破棄されれば、関連するキャッシュエントリは
    /// GC 対象になる。<c>EntityQuery</c> 自体は World が所有するため、
    /// 明示的な Dispose は不要。
    /// </para>
    ///
    /// <para>
    /// <b>公開性</b>: Source Generator の生成コードが呼び出すため <c>public</c>。
    /// ユーザーコードからは直接使わないこと（<see cref="EditorBrowsableState.Never"/>）。
    /// </para>
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ModuleQueryCache
    {
        private static readonly ConditionalWeakTable<World, Dictionary<object, EntityQuery>> s_cache
            = new ConditionalWeakTable<World, Dictionary<object, EntityQuery>>();

        /// <summary>
        /// キャッシュにヒットすれば既存のクエリを、なければ <paramref name="factory"/> を
        /// 呼び出して生成しキャッシュする。
        /// </summary>
        /// <param name="em">クエリ作成に使う EntityManager</param>
        /// <param name="owner">
        /// キャッシュキー。呼び出し元のモジュールインスタンス（<c>this</c>）を渡すこと。
        /// </param>
        /// <param name="factory">新規クエリを作る関数。キャッシュヒット時は呼ばれない。</param>
        public static EntityQuery GetOrCreate(
            EntityManager em, object owner, Func<EntityQuery> factory)
        {
            var world = em.World;
            if (world == null || !world.IsCreated || owner == null)
                return factory();

            var dict = s_cache.GetValue(world, _ => new Dictionary<object, EntityQuery>());
            if (dict.TryGetValue(owner, out var cached))
                return cached;

            var query = factory();
            dict[owner] = query;
            return query;
        }

        /// <summary>テスト用: 特定 World のキャッシュをクリアする。</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void ClearForWorld(World world)
        {
            if (world != null && world.IsCreated)
                s_cache.Remove(world);
        }
    }
}
