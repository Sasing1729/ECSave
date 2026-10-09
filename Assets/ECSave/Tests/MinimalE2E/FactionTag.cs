using Unity.Entities;

namespace Game.Domain
{
    public struct FactionTag : ISharedComponentData, System.IEquatable<FactionTag>
    {
        public int FactionId;
        public bool Equals(FactionTag other) => FactionId == other.FactionId;
        public override int GetHashCode() => FactionId;
    }
}
