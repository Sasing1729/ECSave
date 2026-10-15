using System;
using Unity.Entities;

namespace ECSave
{
    public interface ISaveDefinitionBuilder
    {
        SaveDefinitionBuilder<T> RegisterComponent<T>() where T : unmanaged, IComponentData;
        SaveDefinitionBuilder<T> RegisterBuffer<T>() where T : unmanaged, IBufferElementData;
        SaveDefinitionBuilder<T> RegisterSharedComponent<T>() where T : unmanaged, ISharedComponentData;
        SaveDefinitionBuilder<T> RegisterChunkComponent<T>() where T : unmanaged, IComponentData;
        void RegisterIdentity<T>() where T : unmanaged, IComponentData;
        void RegisterMigration<TMigration>() where TMigration : class, new();
        void SelectEntityReference<T>(Func<T, Entity> field) where T : unmanaged, IComponentData;
        void SelectEntityReferences<T>(Func<T, Entity> field) where T : unmanaged, IBufferElementData;
        void GameDataVersion(int version);
        void WorldId(string id);
    }
}
