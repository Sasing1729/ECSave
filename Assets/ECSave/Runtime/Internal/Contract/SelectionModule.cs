using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    public abstract class SelectionModule
    {
        public int RegistrationId { get; internal set; }

        public abstract string FullTypeName { get; }
        public abstract string FieldPath { get; }

        public abstract EntityQuery BuildQuery(EntityManager em);

        public abstract void ExtractSelected(
            EntityQuery query,
            EntityManager em,
            NativeParallelHashSet<Entity> selected);
    }
}
