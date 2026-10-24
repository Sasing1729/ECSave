namespace ECSave.Internal
{
    /// <summary>
    /// 故障注入のエントリポイント。
    ///
    /// <para>
    /// <b>リリースビルドでの挙動</b>: <c>UNITY_EDITOR</c> も
    /// <c>ECSAVE_ENABLE_FAULT_INJECTION</c> も定義されていない場合、
    /// 内部的に常に <c>false</c> を返す Null 実装で固定され、
    /// <see cref="Current"/> プロパティも公開されない。
    /// これにより、リリースビルドのユーザーが誤って故障を注入することはない。
    /// </para>
    ///
    /// <para>
    /// <b>呼び出し側の契約</b>: 呼び出し側は <see cref="ShouldFail"/> のみを
    /// 使うこと。<see cref="Current"/> は Editor とテストからのみ設定される。
    /// </para>
    /// </summary>
    public static class FaultInjector
    {
        private sealed class NullInjector : IFaultInjector
        {
            public bool ShouldFail(string phase) => false;
        }

#if UNITY_EDITOR || ECSAVE_ENABLE_FAULT_INJECTION
        private static IFaultInjector s_current = new NullInjector();

        public static IFaultInjector Current
        {
            get => s_current;
            set => s_current = value ?? new NullInjector();
        }
#else
        // リリースビルドでは null 実装で固定。Current プロパティは存在しない。
        private static readonly IFaultInjector s_current = new NullInjector();
#endif

        public static bool ShouldFail(string phase) => s_current.ShouldFail(phase);
    }
}
