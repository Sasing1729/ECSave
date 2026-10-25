namespace ECSave
{
    /// <summary>
    /// <see cref="SaveRegistrationException.Code"/> で使うエラーコード定数。
    ///
    /// <para>
    /// <b>命名規約</b>: すべてのコードは <c>ECSAVERNNN</c> 形式（英字 + 数字のみ）。
    /// ハイフンその他の記号を含めてはならない。
    /// Roslyn の診断 ID 規約（<c>^[a-zA-Z][a-zA-Z0-9]*$</c>）に準拠させ、
    /// 実行時例外コードと Roslyn 診断 ID を同じ形式で扱えるようにするため。
    /// ハイフン付きコードは、ログ集約・監視・テスト検証のいずれかで
    /// クラッシュを引き起こす（過去に発生）。
    /// </para>
    ///
    /// <para>
    /// <b>番号の意味</b>: 連番ではなく、発生源ごとにブロック分けされている。
    /// R001〜R019 はメインの登録系、R020〜R029 は SubScene 系、R030〜 は Prefab 系。
    /// 番号そのものはログ・サポート対応で参照されるため、既存の値は変更しないこと。
    /// </para>
    ///
    /// <para>
    /// <b>欠番</b>:
    /// <list type="bullet">
    ///   <item>R004, R009, R010, R012〜R015, R017〜R019, R026〜R029 は未使用</item>
    ///   <item>R011 は過去に「保留マイグレーション検出」で使用されていたが、
    ///         到達不能コードのため削除された（P0 参照）。番号は再利用しないこと</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class RegistrationErrorCodes
    {
        // ── メイン登録系（R001〜R019） ──────────────

        /// <summary>同一モジュール／Identity／Selection の重複登録。</summary>
        public const string ModuleAlreadyRegistered = "ECSAVER001";

        /// <summary><c>GameDataVersion</c> が 1 未満。</summary>
        public const string InvalidGameDataVersion = "ECSAVER002";

        /// <summary><c>WorldId</c> が上限（1024 UTF-8 bytes）を超過。</summary>
        public const string WorldIdTooLong = "ECSAVER003";

        /// <summary><c>RenamedFrom</c> に空文字が渡された。</summary>
        public const string RenamedFromEmpty = "ECSAVER005";

        /// <summary><c>RenamedFrom</c> の旧名が現在名と同一。</summary>
        public const string RenamedFromSameAsCurrent = "ECSAVER006";

        /// <summary><c>RenamedFrom</c> の旧名が既に別の新型にマップ済み。</summary>
        public const string RenamedFromConflict = "ECSAVER007";

        /// <summary><c>RegisterMigration&lt;T&gt;()</c> の型が MigrationStepBase も IComponentMigration&lt;,&gt; も実装していない。</summary>
        public const string MigrationTypeInvalid = "ECSAVER008";

        /// <summary>同一スコープ ID に対して異なる binder 型が登録された。</summary>
        public const string ScopeBinderConflict = "ECSAVER016";

        // ── SubScene 系（R020〜R029） ───────────────

        /// <summary>ConfigureSubScene 内で同一の構成メソッドが複数回呼ばれた。</summary>
        public const string SubSceneConfigDuplicate = "ECSAVER020";

        /// <summary><c>RegisterDestroyedEntities</c> が指定されたが <c>RegisterIdentity</c> が無い。</summary>
        public const string TombstoneWithoutIdentity = "ECSAVER021";

        /// <summary>SubScene 設定で競合する値が指定された。</summary>
        public const string ConflictingSetting = "ECSAVER022";

        /// <summary><c>WithTombstoneCapacity</c> に負の値が渡された。</summary>
        public const string NegativeTombstoneCapacity = "ECSAVER023";

        /// <summary>SubScene 設定の enum に定義外の値が渡された。</summary>
        public const string InvalidEnumValue = "ECSAVER024";

        /// <summary>必須の Provider が指定されていない。</summary>
        public const string MissingProvider = "ECSAVER025";

        // ── Prefab 系（R030〜） ─────────────────────

        /// <summary>同一 <c>OldPrefabSchemaHash</c> に対する PrefabMigration が複数登録された。</summary>
        public const string PrefabMigrationDuplicateOldHash = "ECSAVER030";
    }
}
