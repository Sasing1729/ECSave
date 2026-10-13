using ECSave;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public struct GenHealth : IComponentData
    {
        public int Current;
        public int Max;
    }

    public struct GenStunned : IComponentData, IEnableableComponent
    {
        public int FramesRemaining;
    }

    public struct GenPlayerId : IComponentData
    {
        public int Value;
    }

    public struct GenInventoryItem : IBufferElementData
    {
        public int ItemId;
        public int Count;
    }

    public struct GenRef : IComponentData
    {
        public Entity Target;
        public int Value;
    }

    public sealed class GenTestScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterComponent<GenStunned>();
            b.RegisterComponent<GenRef>();
            b.RegisterBuffer<GenInventoryItem>();
            b.RegisterIdentity<GenPlayerId>();
            b.WorldId("GenTestWorld");
        }
    }

    public sealed class GenSecondaryScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenSecondaryWorld");
        }
    }
}
