namespace ECSave.SourceGenerator.Discovery
{
    internal enum RegistrationKind : byte
    {
        Unknown = 0,
        Component = 1,
        Buffer = 2,
        SharedComponent = 3,
        ChunkComponent = 4,
        Identity = 5,
        Migration = 6,
        SelectEntityReference = 7,
        SelectEntityReferences = 8,
    }
}