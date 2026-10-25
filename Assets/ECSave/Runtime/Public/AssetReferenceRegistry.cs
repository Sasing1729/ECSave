using System;
using System.Collections.Generic;
using Unity.Entities.Content;

namespace ECSave
{
    /// <summary>
    /// <see cref="WeakObjectReference{T}"/> と安定 ID（<c>ulong</c>）の双方向マッピング。
    ///
    /// <para>
    /// <b>用途</b>: セーブデータに <see cref="WeakObjectReference{T}"/> をそのまま保存すると、
    /// ビルド依存の内部 ID が壊れる（<c>ECSAVE065</c>）。
    /// 本レジストリを使い、保存時は安定 ID に変換し、ロード時に
    /// 現在のビルドの <see cref="WeakObjectReference{T}"/> に復元する。
    /// </para>
    /// <para>
    /// <b>スコープ</b>: グローバル（静的）。複数 World で共有される。
    /// <see cref="PrefabSchemaRegistry"/> / <see cref="SubSceneTombstone"/> と同じ方針。
    /// </para>
    /// <para>
    /// <b>スレッド安全性</b>: 内部で <c>lock</c> を使うが、ECSave の公開 API はメインスレッド専用
    /// （<see cref="MainThreadDetector"/>）。登録・検索はメインスレッドから行うこと。
    /// </para>
    /// <para>
    /// <b>ID の生成責務</b>: 本レジストリは ID を生成しない。ユーザーのマスターデータ
    /// （<c>ItemDatabase</c> 等）が安定 ID を割り当て、本レジストリに登録する。
    /// </para>
    /// <para>
    /// <b>特殊値</b>: <see cref="AssetRefConstants.NullRef"/>（0）は予約済みで、
    /// 本レジストリには登録できない。<see cref="AssetRefConstants.UnresolvedRef"/>
    /// （<c>ulong.MaxValue</c>）は「保存時に解決不能」を表すユーザー側のマーカーであり、
    /// 本レジストリは解釈しない（<see cref="TryResolve{T}"/> は通常通り <c>false</c> を返す）。
    /// </para>
    /// <para>
    /// <b>Domain Reload 無効時</b>: <see cref="UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded"/>
    /// で自動クリアされる。
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 起動時（AssetDatabase から読み込み後）
    /// AssetReferenceRegistry.Register&lt;ItemMaster&gt;(1UL, ItemDatabase.SwordRef);
    /// AssetReferenceRegistry.Register&lt;ItemMaster&gt;(2UL, ItemDatabase.ShieldRef);
    ///
    /// // 保存時
    /// AssetReferenceRegistry.TryGetId(component.Master, out var id);
    /// var save = new ItemSaveData { MasterId = id };
    ///
    /// // ロード時
    /// if (AssetReferenceRegistry.TryResolve&lt;ItemMaster&gt;(save.MasterId, out var reference))
    /// {
    ///     var component = new ItemData { Master = reference };
    /// }
    /// </code>
    /// </example>
    public static class AssetReferenceRegistry
    {
        private static readonly object s_lock = new object();
        private static readonly Dictionary<Type, object> s_entries = new Dictionary<Type, object>();

        /// <summary>
        /// <paramref name="stableId"/> と <paramref name="reference"/> を登録する。
        ///
        /// <para>
        /// <b>制約</b>:
        /// <list type="bullet">
        ///   <item>
        ///     <paramref name="stableId"/> は <see cref="AssetRefConstants.NullRef"/> 以外
        ///     （0 は「無効」の予約値）
        ///   </item>
        ///   <item>同じ ID に異なる参照を登録しようとすると例外</item>
        ///   <item>同じ参照に異なる ID を登録しようとすると例外</item>
        ///   <item>同じ (ID, 参照) の重複登録は冪等（成功扱い）</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b><see cref="AssetRefConstants.UnresolvedRef"/> の登録</b>:
        /// <c>ulong.MaxValue</c> は「保存時に解決不能」を表すユーザー側のマーカーであり、
        /// 本レジストリへの登録は想定されない。登録しても動作するが、推奨しない。
        /// </para>
        /// </summary>
        public static void Register<T>(ulong stableId, WeakObjectReference<T> reference)
            where T : UnityEngine.Object
        {
            if (stableId == AssetRefConstants.NullRef)
                throw new ArgumentException(
                    $"Stable ID must not be {AssetRefConstants.NullRef} " +
                    $"(AssetRefConstants.NullRef is reserved for 'invalid').",
                    nameof(stableId));

            lock (s_lock)
            {
                var entry = GetOrCreateEntry<T>();
                entry.Register(stableId, reference);
            }
        }

        /// <summary>
        /// 参照から安定 ID を取得する。未登録なら <c>false</c>。
        ///
        /// <para>
        /// <b>前提</b>: <see cref="WeakObjectReference{T}"/> が値等価性を持つこと
        /// （Unity の実装では内部 ID ベースの <c>Equals</c> を提供）。
        /// 等価性が機能しない場合、本メソッドは常に <c>false</c> を返す。
        /// その場合は <see cref="IAssetReferenceCodec{T}"/> を実装し、
        /// ユーザー側のマスターデータで逆引きすること。
        /// </para>
        /// <para>
        /// <b>戻り値</b>: <c>false</c> の場合、<paramref name="stableId"/> は
        /// <see cref="AssetRefConstants.NullRef"/>（0）に設定される。
        /// 呼び出し側が「参照はあるが未登録」を区別したい場合は、
        /// <see cref="AssetRefConstants.UnresolvedRef"/> に置換して保存すること。
        /// </para>
        /// </summary>
        public static bool TryGetId<T>(WeakObjectReference<T> reference, out ulong stableId)
            where T : UnityEngine.Object
        {
            lock (s_lock)
            {
                if (s_entries.TryGetValue(typeof(T), out var obj) && obj is TypeEntry<T> entry)
                    return entry.TryGetId(reference, out stableId);

                stableId = AssetRefConstants.NullRef;
                return false;
            }
        }

        /// <summary>
        /// 安定 ID から参照を取得する。未登録なら <c>false</c>。
        ///
        /// <para>
        /// <b>特殊値の扱い</b>:
        /// <list type="bullet">
        ///   <item><see cref="AssetRefConstants.NullRef"/>: <c>false</c>（登録されていない）</item>
        ///   <item><see cref="AssetRefConstants.UnresolvedRef"/>: <c>false</c>
        ///         （「保存時に解決不能」マーカー。レジストリには登録されていない）</item>
        /// </list>
        /// </para>
        /// </summary>
        public static bool TryResolve<T>(ulong stableId, out WeakObjectReference<T> reference)
            where T : UnityEngine.Object
        {
            lock (s_lock)
            {
                if (s_entries.TryGetValue(typeof(T), out var obj) && obj is TypeEntry<T> entry)
                    return entry.TryResolve(stableId, out reference);

                reference = default;
                return false;
            }
        }

        /// <summary>
        /// 全登録をクリアする（テスト用）。
        /// </summary>
        public static void Clear()
        {
            lock (s_lock) { s_entries.Clear(); }
        }

        /// <summary>
        /// Domain Reload 無効時の静的状態リセット。
        /// <see cref="PrefabSchemaRegistry"/> / <see cref="SubSceneTombstone"/> と同じ方針。
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ResetOnLoad()
        {
            lock (s_lock) { s_entries.Clear(); }
        }

        private static TypeEntry<T> GetOrCreateEntry<T>() where T : UnityEngine.Object
        {
            if (s_entries.TryGetValue(typeof(T), out var existing) && existing is TypeEntry<T> entry)
                return entry;

            var newEntry = new TypeEntry<T>();
            s_entries[typeof(T)] = newEntry;
            return newEntry;
        }

        private sealed class TypeEntry<T> where T : UnityEngine.Object
        {
            private readonly Dictionary<ulong, WeakObjectReference<T>> _byId
                = new Dictionary<ulong, WeakObjectReference<T>>();

            private readonly Dictionary<WeakObjectReference<T>, ulong> _byReference
                = new Dictionary<WeakObjectReference<T>, ulong>();

            public void Register(ulong stableId, WeakObjectReference<T> reference)
            {
                if (_byId.TryGetValue(stableId, out var existing) && !existing.Equals(reference))
                {
                    throw new InvalidOperationException(
                        $"Stable ID {stableId} is already registered with a different reference " +
                        $"for type {typeof(T).FullName}.");
                }

                if (_byReference.TryGetValue(reference, out var existingId) && existingId != stableId)
                {
                    throw new InvalidOperationException(
                        $"Reference is already registered with stable ID {existingId} " +
                        $"for type {typeof(T).FullName}. Attempted to register ID {stableId}.");
                }

                _byId[stableId] = reference;
                _byReference[reference] = stableId;
            }

            public bool TryGetId(WeakObjectReference<T> reference, out ulong stableId)
                => _byReference.TryGetValue(reference, out stableId);

            public bool TryResolve(ulong stableId, out WeakObjectReference<T> reference)
                => _byId.TryGetValue(stableId, out reference);
        }
    }
}
