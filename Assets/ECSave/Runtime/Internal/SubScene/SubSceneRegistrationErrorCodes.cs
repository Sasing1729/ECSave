namespace ECSave.Internal.SubScene
{
    /// <summary>
    /// <see cref="ECSave.RegistrationErrorCodes"/> への互換エイリアス。
    ///
    /// <para>
    /// 旧実装は SubScene 専用に本クラスで定数を管理していたが、
    /// コード体系はメイン登録系と共通の <c>ECSAVE-RXXX</c> であるため、
    /// <see cref="ECSave.RegistrationErrorCodes"/> に統合した。
    /// </para>
    /// <para>
    /// <b>互換性</b>: 既存の呼び出し箇所（<see cref="SubSceneDefinitionBuilderImpl"/>）を
    /// 壊さないため、本クラスは <see cref="ECSave.RegistrationErrorCodes"/> の
    /// 該当定数への参照を保持する。新規コードは直接
    /// <see cref="ECSave.RegistrationErrorCodes"/> を参照すること。
    /// </para>
    /// </summary>
    internal static class SubSceneRegistrationErrorCodes
    {
        public const string ConfigDuplicate = RegistrationErrorCodes.SubSceneConfigDuplicate;
        public const string TombstoneWithoutIdentity = RegistrationErrorCodes.TombstoneWithoutIdentity;
        public const string ConflictingSetting = RegistrationErrorCodes.ConflictingSetting;
        public const string NegativeTombstoneCapacity = RegistrationErrorCodes.NegativeTombstoneCapacity;
        public const string InvalidEnumValue = RegistrationErrorCodes.InvalidEnumValue;
        public const string MissingProvider = RegistrationErrorCodes.MissingProvider;
    }
}
