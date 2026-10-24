#if UNITY_EDITOR || ECSAVE_ENABLE_FAULT_INJECTION
namespace ECSave.Internal
{
    /// <summary>
    /// テスト用の故障注入インターフェース。
    ///
    /// <para>
    /// <b>条件付きコンパイル</b>: 本インターフェースは
    /// <c>UNITY_EDITOR</c> または <c>ECSAVE_ENABLE_FAULT_INJECTION</c>
    /// シンボル定義時のみコンパイルされる。リリースビルドでは
    /// 型自体が存在しない。
    /// </para>
    /// </summary>
    public interface IFaultInjector
    {
        bool ShouldFail(string phase);
    }
}
#endif