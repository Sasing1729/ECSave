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

        /// <summary>
        /// FixedString の自動修復（P7.2）を無効化する。
        /// 無効化した場合、Deserialize 時に切詰め疑いを検出しても警告のみで、
        /// データは変更しない。デフォルトは有効（自動修復する）。
        /// </summary>
        void DisableAutoRepairFixedString();
    }
}
