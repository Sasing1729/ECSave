namespace ECSave.Internal.SubScene
{
    internal static class SubSceneRegistrationErrorCodes
    {
        public const string ConfigDuplicate = "ECSAVE-R020";
        public const string TombstoneWithoutIdentity = "ECSAVE-R021";
        public const string ConflictingSetting = "ECSAVE-R022";
        public const string NegativeTombstoneCapacity = "ECSAVE-R023";
        public const string InvalidEnumValue = "ECSAVE-R024";
        public const string MissingProvider = "ECSAVE-R025";
    }
}
