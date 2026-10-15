using Unity.Entities;

namespace ECSave
{
    public interface IComponentMigration<TOld, TNew>
        where TOld : unmanaged, IComponentData
        where TNew : unmanaged, IComponentData
    {
        TNew Migrate(in TOld old);
    }
}
