namespace ECSave.SourceGenerator.Emission
{
    internal enum ModuleClass
    {
        Unknown = 0,
        Plain,
        Enableable,
        Shared,
        Buffer,
        BufferEnableable,
        Chunk,
        Identity,
    }

    internal static class TypeModuleClassifier
    {
        public const string IComponentDataMetadataName = "Unity.Entities.IComponentData";
        public const string IBufferElementDataMetadataName = "Unity.Entities.IBufferElementData";
        public const string ISharedComponentDataMetadataName = "Unity.Entities.ISharedComponentData";
        public const string IEnableableComponentMetadataName = "Unity.Entities.IEnableableComponent";

        public static ModuleClass Classify(
            Microsoft.CodeAnalysis.ITypeSymbol type,
            Discovery.RegistrationKind kind)
        {
            if (type == null) return ModuleClass.Unknown;

            if (kind == Discovery.RegistrationKind.Identity)
                return ModuleClass.Identity;

            bool isBuffer = Implements(type, IBufferElementDataMetadataName);
            bool isShared = Implements(type, ISharedComponentDataMetadataName);
            bool isComponent = Implements(type, IComponentDataMetadataName);
            bool isEnableable = Implements(type, IEnableableComponentMetadataName);

            if (kind == Discovery.RegistrationKind.ChunkComponent)
                return ModuleClass.Chunk;

            if (isShared)
                return ModuleClass.Shared;

            if (isBuffer)
                return isEnableable ? ModuleClass.BufferEnableable : ModuleClass.Buffer;

            if (isComponent)
                return isEnableable ? ModuleClass.Enableable : ModuleClass.Plain;

            return ModuleClass.Unknown;
        }

        public static string ToBaseClassName(ModuleClass cls)
        {
            switch (cls)
            {
                case ModuleClass.Plain: return "SaveTypeModulePlain";
                case ModuleClass.Enableable: return "SaveTypeModuleEnableable";
                case ModuleClass.Shared: return "SaveTypeModuleShared";
                case ModuleClass.Buffer: return "SaveTypeModuleBuffer";
                case ModuleClass.BufferEnableable: return "SaveTypeModuleBufferEnableable";
                case ModuleClass.Chunk: return "SaveTypeModuleChunk";
                case ModuleClass.Identity: return "IdentityModule";
                default: return null;
            }
        }

        public static bool IsModuleClass(ModuleClass cls)
            => cls != ModuleClass.Unknown && cls != ModuleClass.Identity;

        public static bool IsIdentityClass(ModuleClass cls)
            => cls == ModuleClass.Identity;

        private static bool Implements(Microsoft.CodeAnalysis.ITypeSymbol type, string metadataName)
        {
            if (type == null) return false;
            foreach (var i in type.AllInterfaces)
                if (i.ToDisplayString() == metadataName) return true;
            return false;
        }
    }
}
