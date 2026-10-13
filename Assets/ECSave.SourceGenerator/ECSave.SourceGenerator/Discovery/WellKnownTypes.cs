using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Discovery
{
    internal static class WellKnownTypes
    {
        public const string SaveScopeMetadataName = "ECSave.SaveScope";
        public const string ISaveDefinitionBuilderMetadataName = "ECSave.ISaveDefinitionBuilder";
        public const string SaveDefinitionBuilderMetadataPrefix = "ECSave.SaveDefinitionBuilder<";

        public const string ConfigureMethodName = "Configure";

        public const string RegisterComponentMethodName = "RegisterComponent";
        public const string RegisterBufferMethodName = "RegisterBuffer";
        public const string RegisterSharedComponentMethodName = "RegisterSharedComponent";
        public const string RegisterChunkComponentMethodName = "RegisterChunkComponent";
        public const string RegisterIdentityMethodName = "RegisterIdentity";
        public const string RegisterMigrationMethodName = "RegisterMigration";
        public const string SelectEntityReferenceMethodName = "SelectEntityReference";
        public const string SelectEntityReferencesMethodName = "SelectEntityReferences";

        public const string IComponentDataMetadataName = "Unity.Entities.IComponentData";
        public const string IBufferElementDataMetadataName = "Unity.Entities.IBufferElementData";
        public const string ISharedComponentDataMetadataName = "Unity.Entities.ISharedComponentData";
        public const string IEnableableComponentMetadataName = "Unity.Entities.IEnableableComponent";
        public const string EntityMetadataName = "Unity.Entities.Entity";

        public static bool IsSaveScope(INamedTypeSymbol type)
        {
            if (type == null) return false;

            INamedTypeSymbol current;
            try
            {
                current = type.BaseType;
            }
            catch
            {
                return false;
            }

            int guard = 0;
            while (current != null && guard < 64)
            {
                guard++;
                string name;
                try
                {
                    name = current.ToDisplayString();
                }
                catch
                {
                    return false;
                }

                if (name == SaveScopeMetadataName)
                    return true;

                try
                {
                    current = current.BaseType;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public static bool IsBuilderType(INamedTypeSymbol type)
        {
            if (type == null) return false;
            string name;
            try
            {
                name = type.ToDisplayString();
            }
            catch
            {
                return false;
            }

            if (name == ISaveDefinitionBuilderMetadataName) return true;
            if (name.StartsWith(SaveDefinitionBuilderMetadataPrefix)) return true;
            return false;
        }

        public static RegistrationKind MapMethodName(string methodName)
        {
            switch (methodName)
            {
                case RegisterComponentMethodName: return RegistrationKind.Component;
                case RegisterBufferMethodName: return RegistrationKind.Buffer;
                case RegisterSharedComponentMethodName: return RegistrationKind.SharedComponent;
                case RegisterChunkComponentMethodName: return RegistrationKind.ChunkComponent;
                case RegisterIdentityMethodName: return RegistrationKind.Identity;
                case RegisterMigrationMethodName: return RegistrationKind.Migration;
                case SelectEntityReferenceMethodName: return RegistrationKind.SelectEntityReference;
                case SelectEntityReferencesMethodName: return RegistrationKind.SelectEntityReferences;
                default: return RegistrationKind.Unknown;
            }
        }
    }
}
