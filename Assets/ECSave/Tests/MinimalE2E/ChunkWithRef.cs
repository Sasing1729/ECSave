using Unity.Entities;

namespace Game.Domain
{
    /// <summary>
    /// B-5 検証用: Chunk コンポーネント内に Entity 参照を持つ型。
    /// sizeof(ChunkWithRef) = 16 (Entity 8 + int 4 + padding 4)
    /// EntityRefOffsets = [0] → RecordStride = 16 - 4 = 12
    /// </summary>
    public struct ChunkWithRef : IComponentData
    {
        public Entity Target;
        public int Value;
    }
}
