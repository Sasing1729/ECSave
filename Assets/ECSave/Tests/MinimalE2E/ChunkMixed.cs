using Unity.Collections;
using Unity.Entities;

namespace Game.Domain
{
    /// <summary>
    /// B-5 / P4 検証用: FixedString + Entity を混在させた Chunk コンポーネント。
    ///
    /// <para>raw レイアウト (sizeof = 44):</para>
    /// <code>
    /// offset  0: FixedString32Bytes Name (32B, align 2)
    /// offset 32: Entity Target         ( 8B, align 4)
    /// offset 40: int Value             ( 4B, align 4)
    /// </code>
    ///
    /// <para>dataStride レイアウト (RecordStride = 44 - 4 = 40):</para>
    /// <code>
    /// offset  0: FixedString32Bytes Name (32B)
    /// offset 32: uint Target_pid          ( 4B)
    /// offset 36: int Value                ( 4B)
    /// </code>
    ///
    /// <para>EntityRefOffsets = [32]</para>
    /// </summary>
    public struct ChunkMixed : IComponentData
    {
        public FixedString32Bytes Name;
        public Entity Target;
        public int Value;
    }
}
