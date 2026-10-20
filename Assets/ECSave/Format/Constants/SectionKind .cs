namespace ECSave.Format
{
    public enum SectionKind : byte
    {
        Unknown = 0,

        Identity = 1,
        Component = 2,
        Buffer = 3,
        Shared = 4,
        ChunkComponent = 5,

        SubSceneState = 6,
        SubSceneTombstone = 7,
        PrefabIdentity = 8,
    }
}
