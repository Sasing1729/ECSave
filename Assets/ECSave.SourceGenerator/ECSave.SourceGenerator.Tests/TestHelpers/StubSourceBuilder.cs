namespace ECSave.SourceGenerator.Tests.TestHelpers
{
    internal static class StubSourceBuilder
    {
        public static string BuilderStubs()
        {
            return @"
namespace ECSave
{
    public interface ISaveDefinitionBuilder
    {
        SaveDefinitionBuilder<T> RegisterComponent<T>() where T : unmanaged, Unity.Entities.IComponentData;
        SaveDefinitionBuilder<T> RegisterBuffer<T>() where T : unmanaged, Unity.Entities.IBufferElementData;
        SaveDefinitionBuilder<T> RegisterSharedComponent<T>() where T : unmanaged, Unity.Entities.ISharedComponentData;
        SaveDefinitionBuilder<T> RegisterChunkComponent<T>() where T : unmanaged, Unity.Entities.IComponentData;
        void RegisterIdentity<T>() where T : unmanaged, Unity.Entities.IComponentData;
        void RegisterMigration<TMigration>() where TMigration : class, new();
        void SelectEntityReference<T>(System.Func<T, Unity.Entities.Entity> field) where T : unmanaged, Unity.Entities.IComponentData;
        void SelectEntityReferences<T>(System.Func<T, Unity.Entities.Entity> field) where T : unmanaged, Unity.Entities.IBufferElementData;
        void GameDataVersion(int version);
        void WorldId(string id);
    }

    public class SaveDefinitionBuilder<T>
    {
        public SaveDefinitionBuilder<T> WithoutEnableableState() { return this; }
        public SaveDefinitionBuilder<T> WithAll<TFilter>() where TFilter : unmanaged, Unity.Entities.IComponentData { return this; }
        public SaveDefinitionBuilder<T> WithAny<TFilter>() where TFilter : unmanaged, Unity.Entities.IComponentData { return this; }
        public SaveDefinitionBuilder<T> WithNone<TFilter>() where TFilter : unmanaged, Unity.Entities.IComponentData { return this; }
        public SaveDefinitionBuilder<T> RenamedFrom(string oldFullName) { return this; }
    }

    public abstract class SaveScope
    {
        protected abstract void Configure(ISaveDefinitionBuilder builder);
    }
}

namespace ECSave.Internal
{
    public interface IComponentMigration<TOld, TNew>
    {
        TNew Migrate(in TOld old);
    }
}

namespace Unity.Entities
{
    public interface IComponentData { }
    public interface IBufferElementData { }
    public interface ISharedComponentData { }
    public interface IEnableableComponent { }
    public struct Entity { }
}

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4,
    }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}
";
        }
    }
}
