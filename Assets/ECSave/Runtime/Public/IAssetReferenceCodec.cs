using Unity.Entities.Content;

namespace ECSave
{
    /// <summary>
    /// <see cref="WeakObjectReference{T}"/> と安定 ID（<c>ulong</c>）の相互変換を提供する。
    ///
    /// <para>
    /// <b>用途</b>: 保存対象のコンポーネントに <see cref="WeakObjectReference{T}"/> が含まれる場合、
    /// そのまま保存するとビルド間で参照が壊れる（<c>ECSAVE065</c>）。
    /// 本インターフェースを実装し、ユーザー側のマスターデータ（<c>ItemDatabase</c> 等）から
    /// 安定 ID を導出することで、ビルド間で復元可能な保存形式を実現する。
    /// </para>
    /// <para>
    /// <b>特殊値</b>:
    /// <list type="bullet">
    ///   <item><see cref="AssetRefConstants.NullRef"/> (0): 参照が無効（default）</item>
    ///   <item><see cref="AssetRefConstants.UnresolvedRef"/> (<c>ulong.MaxValue</c>):
    ///         保存時に stableId を解決できなかった</item>
    /// </list>
    /// <see cref="GetStableId"/> は未登録時に <see cref="AssetRefConstants.NullRef"/> を返し、
    /// 呼び出し側（ユーザーの module / 変換コード）が必要に応じて
    /// <see cref="AssetRefConstants.UnresolvedRef"/> に置換する。
    /// </para>
    /// <para>
    /// <b>設計方針</b>: ECSave は ID の生成方法を知らない（知る必要がない）。
    /// ID の付与・検索はユーザーのマスターデータが責務を持つ。
    /// 本インターフェースは「変換の型付け」のみを提供する。
    /// </para>
    /// </summary>
    /// <typeparam name="T">
    /// アセット型。<see cref="WeakObjectReference{T}"/> の制約に従い、
    /// <c>UnityEngine.Object</c> 派生であること。
    /// </typeparam>
    public interface IAssetReferenceCodec<T> where T : UnityEngine.Object
    {
        /// <summary>
        /// 参照から安定 ID を返す。未登録・未解決の場合は
        /// <see cref="AssetRefConstants.NullRef"/>（= 0）を返すこと。
        ///
        /// <para>
        /// <b>戻り値の扱い</b>:
        /// <list type="bullet">
        ///   <item><see cref="AssetRefConstants.NullRef"/>: 参照が無効</item>
        ///   <item>その他の値: 有効な stableId</item>
        /// </list>
        /// 呼び出し側が「参照はあるが stableId 未登録」を区別したい場合、
        /// <see cref="AssetRefConstants.UnresolvedRef"/> に置換して保存すること。
        /// </para>
        /// </summary>
        ulong GetStableId(WeakObjectReference<T> reference);

        /// <summary>
        /// 安定 ID から参照を復元する。解決できない場合は <c>false</c> を返す。
        ///
        /// <para>
        /// <b>入力の扱い</b>:
        /// <list type="bullet">
        ///   <item><see cref="AssetRefConstants.NullRef"/>: <c>false</c> を返す
        ///         （呼び出し側は <c>default(WeakObjectReference&lt;T&gt;)</c> を使う）</item>
        ///   <item><see cref="AssetRefConstants.UnresolvedRef"/>: <c>false</c> を返す
        ///         （呼び出し側は既定値を使い、必要なら警告を記録する）</item>
        ///   <item>その他: 解決を試みる</item>
        /// </list>
        /// </para>
        /// </summary>
        bool TryResolve(ulong stableId, out WeakObjectReference<T> reference);
    }
}
