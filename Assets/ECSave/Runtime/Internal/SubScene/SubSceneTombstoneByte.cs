using Unity.Entities;

namespace ECSave.Internal.SubScene
{
    /// <summary>
    /// Tombstone Registry 用の非ジェネリックバッファ要素。
    /// レコードは [8B typeHash][4B length][length B payload] の手動エンコード。
    /// </summary>
    public struct SubSceneTombstoneByte : IBufferElementData
    {
        public byte Value;
    }
}
