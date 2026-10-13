using Unity.Entities;

namespace ECSave
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial class ECSaveSnapshotGroup : ComponentSystemGroup
    {
    }
}
