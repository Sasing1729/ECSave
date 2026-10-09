namespace ECSave.Internal
{
    public interface IFaultInjector
    {
        bool ShouldFail(string phase);
    }
}
