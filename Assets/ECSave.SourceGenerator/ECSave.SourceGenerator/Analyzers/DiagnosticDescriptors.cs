using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Analyzers
{
    internal static class DiagnosticDescriptors
    {
        private const string Category = "ECSave";

        private static DiagnosticDescriptor Error(string id, string title, string message)
            => new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static DiagnosticDescriptor Warning(string id, string title, string message)
            => new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SelectFieldFormat = Error(
            DiagnosticIds.SelectFieldFormat,
            "Select field format is invalid",
            "SelectEntityReference(s) requires a member access expression such as x => x.Field");

        public static readonly DiagnosticDescriptor SelectFieldNotEntity = Error(
            DiagnosticIds.SelectFieldNotEntity,
            "Selected field must be of type Entity",
            "Selected field '{0}' is not of type Unity.Entities.Entity");

        public static readonly DiagnosticDescriptor SelectFieldMissing = Error(
            DiagnosticIds.SelectFieldMissing,
            "Selected field does not exist",
            "Field '{0}' does not exist on the target type");

        public static readonly DiagnosticDescriptor IdentityNotComponent = Error(
            DiagnosticIds.IdentityNotComponent,
            "Identity type must implement IComponentData",
            "Identity type '{0}' must implement Unity.Entities.IComponentData");

        public static readonly DiagnosticDescriptor IdentityBufferNotAllowed = Error(
            DiagnosticIds.IdentityBufferNotAllowed,
            "Identity type cannot be a buffer element",
            "Identity type '{0}' cannot implement IBufferElementData");

        public static readonly DiagnosticDescriptor IdentitySharedNotAllowed = Error(
            DiagnosticIds.IdentitySharedNotAllowed,
            "Identity type cannot be a shared component",
            "Identity type '{0}' cannot implement ISharedComponentData");

        public static readonly DiagnosticDescriptor IdentityGenericNotAllowed = Error(
            DiagnosticIds.IdentityGenericNotAllowed,
            "Identity type cannot be generic",
            "Identity type '{0}' cannot be generic");

        public static readonly DiagnosticDescriptor IdentityExplicitNotAllowed = Error(
            DiagnosticIds.IdentityExplicitNotAllowed,
            "Identity type cannot use LayoutKind.Explicit",
            "Identity type '{0}' cannot use LayoutKind.Explicit");

        public static readonly DiagnosticDescriptor IdentityTooManyFields = Error(
            DiagnosticIds.IdentityTooManyFields,
            "Identity type has too many fields",
            "Identity type '{0}' has {1} fields; maximum is 32");

        public static readonly DiagnosticDescriptor IdentityTooDeep = Error(
            DiagnosticIds.IdentityTooDeep,
            "Identity type has nesting deeper than allowed",
            "Identity type '{0}' nesting depth exceeds maximum of 4");

        public static readonly DiagnosticDescriptor IdentityEntityNotAllowed = Error(
            DiagnosticIds.IdentityEntityNotAllowed,
            "Identity type cannot contain Entity fields",
            "Identity type '{0}' cannot contain Entity fields");

        public static readonly DiagnosticDescriptor IdentityExplicitRejected = Error(
            DiagnosticIds.IdentityExplicitRejected,
            "Identity type with Explicit layout is rejected",
            "Identity type '{0}' cannot use LayoutKind.Explicit");

        public static readonly DiagnosticDescriptor SaveTypeAutoWithEntity = Error(
            DiagnosticIds.SaveTypeAutoWithEntity,
            "LayoutKind.Auto with Entity field is not supported",
            "Save type '{0}' uses LayoutKind.Auto and contains Entity fields; only LayoutKind.Sequential or Explicit are supported");

        public static readonly DiagnosticDescriptor SaveTypeAutoWithoutEntity = Error(
            DiagnosticIds.SaveTypeAutoWithoutEntity,
            "LayoutKind.Auto is not supported",
            "Save type '{0}' uses LayoutKind.Auto; only LayoutKind.Sequential or Explicit are supported");

        public static readonly DiagnosticDescriptor SaveTypePrivateField = Error(
            DiagnosticIds.SaveTypePrivateField,
            "Save type fields must be public or internal",
            "Field '{0}.{1}' must be public or internal");

        public static readonly DiagnosticDescriptor SaveTypeTooLarge = Error(
            DiagnosticIds.SaveTypeTooLarge,
            "Save type is too large",
            "Save type '{0}' has a field at offset {1} with size {2}; total size exceeds 65535");

        public static readonly DiagnosticDescriptor SaveTypeExplicitOverlapUncovered = Error(
            DiagnosticIds.SaveTypeExplicitOverlapUncovered,
            "Explicit layout overlap group cannot be represented",
            "Save type '{0}' has an overlap group containing Entity fields that no single field covers entirely");

        public static readonly DiagnosticDescriptor SaveTypePackNotSupported = Error(
            DiagnosticIds.SaveTypePackNotSupported,
            "StructLayout(Pack) is not supported",
            "Save type '{0}' uses StructLayout(Pack); this is not supported");

        public static readonly DiagnosticDescriptor MigrationTypeInvalid = Error(
            DiagnosticIds.MigrationTypeInvalid,
            "Migration type argument is invalid",
            "Migration target type '{0}' cannot contain Entity, FixedString, FixedList, or generic types");

        public static readonly DiagnosticDescriptor MigrationNoDefaultCtor = Error(
            DiagnosticIds.MigrationNoDefaultCtor,
            "Migration class must have a parameterless constructor",
            "Migration class '{0}' must have a public parameterless constructor");

        public static readonly DiagnosticDescriptor SaveScopeInvalid = Error(
            DiagnosticIds.SaveScopeInvalid,
            "SaveScope class is invalid",
            "SaveScope derived class '{0}' must be non-generic and accessible");

        public static readonly DiagnosticDescriptor TypeArgumentNotStatic = Warning(
            DiagnosticIds.TypeArgumentNotStatic,
            "Type argument cannot be resolved statically",
            "Type argument for '{0}' cannot be resolved statically; it will be collected as a runtime check");

        public static readonly DiagnosticDescriptor AnalysisFailed = new DiagnosticDescriptor(
            DiagnosticIds.AnalysisFailed,
            "SaveScope analysis failed",
            "Analysis of '{0}' failed during code generation: {1}. " +
            "The scope has been generated with partial information.",
            "ECSave",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
