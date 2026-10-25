using System;

namespace ECSave
{
    /// <summary>
    /// <see cref="SaveScope"/> が使用する binder 型を明示する。
    ///
    /// <para>
    /// <b>用途</b>: 複数の SaveScope でモジュール登録・SubScene 設定を共有したい場合、
    /// または auto-generated binder の代わりに手書き binder を使いたい場合。
    /// </para>
    /// <para>
    /// <b>binder の要件</b>:
    /// <list type="bullet">
    ///   <item><c>ECSave.Internal.Contract.ISaveModuleBinder</c> を実装する</item>
    ///   <item>非 abstract・非 generic の具象クラスである</item>
    ///   <item>public パラメータなしコンストラクタを持つ</item>
    /// </list>
    /// いずれかを満たさない場合、Source Generator が <c>ECSAVE061</c>〜<c>ECSAVE063</c> を発行する。
    /// </para>
    /// <para>
    /// <b>SubScene を共有する場合</b>: binder 型が
    /// <c>ECSave.Internal.Contract.ISubSceneBinder</c> も実装していれば、
    /// <c>BindSubScene</c> が呼ばれる。これにより、複数 Scope で SubScene 設定を共有できる。
    /// </para>
    /// <para>
    /// <b>未指定の場合</b>: Source Generator が <c>XxxScope_ECSaveBindings</c> クラスを自動生成する
    /// （現行挙動）。
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// [SaveScopeBinder(typeof(SharedBinder))]
    /// public sealed class ModeAScope : SaveScope
    /// {
    ///     protected override void Configure(ISaveDefinitionBuilder b) { }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class SaveScopeBinderAttribute : Attribute
    {
        public Type BinderType { get; }

        public SaveScopeBinderAttribute(Type binderType)
        {
            BinderType = binderType;
        }
    }
}
