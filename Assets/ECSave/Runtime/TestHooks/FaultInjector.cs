namespace ECSave.Internal
{
    public static class FaultInjector
    {
        private sealed class NullInjector : IFaultInjector
        {
            public bool ShouldFail(string phase) => false;
        }

        private static IFaultInjector s_current = new NullInjector();

        public static IFaultInjector Current
        {
            get => s_current;
            set => s_current = value ?? new NullInjector();
        }

        public static bool ShouldFail(string phase) => s_current.ShouldFail(phase);
    }
}
