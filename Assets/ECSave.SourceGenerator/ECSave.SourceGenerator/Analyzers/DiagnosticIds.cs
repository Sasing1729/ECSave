namespace ECSave.SourceGenerator.Analyzers
{
    public static class DiagnosticIds
    {
        public const string SelectFieldFormat = "ECSAVE001";
        public const string SelectFieldNotEntity = "ECSAVE002";
        public const string SelectFieldMissing = "ECSAVE003";

        public const string IdentityNotComponent = "ECSAVE010";
        public const string IdentityBufferNotAllowed = "ECSAVE011";
        public const string IdentitySharedNotAllowed = "ECSAVE012";
        public const string IdentityGenericNotAllowed = "ECSAVE013";
        public const string IdentityExplicitNotAllowed = "ECSAVE014";
        public const string IdentityTooManyFields = "ECSAVE015";
        public const string IdentityTooDeep = "ECSAVE016";
        public const string IdentityEntityNotAllowed = "ECSAVE017";
        public const string IdentityExplicitRejected = "ECSAVE018";

        public const string SaveTypeAutoWithEntity = "ECSAVE022";
        public const string SaveTypeAutoWithoutEntity = "ECSAVE029";
        public const string SaveTypePrivateField = "ECSAVE023";
        public const string SaveTypeTooLarge = "ECSAVE026";
        public const string SaveTypeExplicitOverlapUncovered = "ECSAVE028";
        public const string SaveTypePackNotSupported = "ECSAVE025";

        public const string MigrationTypeInvalid = "ECSAVE030";
        public const string MigrationNoDefaultCtor = "ECSAVE031";

        public const string SaveScopeInvalid = "ECSAVE040";
        public const string TypeArgumentNotStatic = "ECSAVE041";

        public const string AnalysisFailed = "ECSAVE-R018";
    }
}
