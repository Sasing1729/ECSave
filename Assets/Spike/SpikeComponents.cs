using Unity.Entities;
using Unity.Mathematics;

namespace SaveSpike
{
    public struct SpikeId : IComponentData { public int Value; }
    public struct SpikeHealth : IComponentData { public int Current; public int Max; }
    public struct SpikeTarget : IComponentData { public Entity Value; }
    public struct SpikeStunned : IComponentData, IEnableableComponent { }
    public struct SpikeInventory : IBufferElementData { public int ItemId; public int Count; }
    public struct SpikePlayer : IComponentData { }
    public struct SpikePosition : IComponentData { public float3 Value; }

    /// <summary>カメラ相当。登録型ではない（描画系の未登録Componentを模す）。</summary>
    public struct SpikeCameraTag : IComponentData { }

    /// <summary>ユースケース確認用のドロップアイテムタグ（登録型のみ、識別なし）。</summary>
    public struct SpikeDroppedItemTag : IComponentData { }

    /// <summary>2つ目の識別型（S10：複数識別型の検出用）。保存はしない。</summary>
    public struct SpikeAltId : IComponentData { public int Value; }

    /// <summary>未登録Component（値あり）。ロードで保持／喪失されるかの確認用（S8）。</summary>
    public struct SpikeUnregistered : IComponentData { public int Value; }
}
