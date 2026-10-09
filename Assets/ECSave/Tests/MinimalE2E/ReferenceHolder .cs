using Unity.Entities;

namespace Game.Domain
{
    public struct ReferenceHolder : IComponentData
    {
        public Entity Target;
        public int Value;
    }
}
