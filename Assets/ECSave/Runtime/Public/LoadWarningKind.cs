namespace ECSave
{
    public enum LoadWarningKind : byte
    {
        /// <summary>
        /// Entity 参照が保存時に解決できなかった（保存対象の Entity 集合に含まれない）。
        /// ロード後の参照は <c>Entity.Null</c> に置換される。
        /// </summary>
        EntityReferenceUnresolved = 0,

        /// <summary>
        /// セーブデータ内のセクションが現在の SaveDefinition に登録されていない型に対応する。
        /// 該当セクションはスキップされ、データは復元されない。
        /// </summary>
        UnknownSectionIgnored = 1,

        /// <summary>
        /// チャンクコンポーネントの「マージ」が発生したことを示す。
        ///
        /// <para>
        /// <b>発生条件</b>: 保存時に異なるチャンク（Archetype）に属していた Entity が、
        /// ロード後の World では同じチャンクに属する場合。
        /// </para>
        /// </summary>
        ChunkComponentMerged = 2,

        /// <summary>
        /// SubScene の GUID またはロード状態が保存時と現在で一致しない。
        ///
        /// <para>
        /// <b>発生条件</b>:
        /// <see cref="ContentVerificationMode.GuidOnly"/> または
        /// <see cref="ContentVerificationMode.GuidAndContentHash"/> が指定され、
        /// <see cref="SubSceneDefinition.SceneStateProvider"/> が現在のシーン状態を返すとき、
        /// 保存時の <see cref="SceneLoadInfo.Guid"/> / <see cref="SceneLoadInfo.State"/> と
        /// 現在の値が一致しない場合。
        /// </para>
        /// <para>
        /// <b>比較対象外</b>: 保存側・現在側いずれかの <see cref="SceneLoadState"/> が
        /// <c>Loading</c> の場合は比較対象外（遷移中の一時状態を「不一致」と誤判定しないため）。
        /// </para>
        /// <para>
        /// <b>SaveEntityIndex</b> には、保存データ内のシーンエントリのインデックスが入る。
        /// </para>
        /// </summary>
        SceneLoadStateMismatch = 3,

        /// <summary>
        /// シーン復元コールバックが <see cref="SceneLoadOutcome.Success"/> = false を返した
        /// （<see cref="SceneRestoreMode.Optional"/> の場合のみ警告、Required はエラー）。
        /// </summary>
        SceneRestoreFailed = 4,

        /// <summary>
        /// SubScene のコンテンツハッシュが保存時と現在で一致しない。
        /// <see cref="ContentVerificationMode.GuidAndContentHash"/> 指定時のみ。
        /// </summary>
        SceneContentHashMismatch = 5,

        /// <summary>
        /// Prefab のスキーマ変更に対してマイグレーションが実行された。
        /// <see cref="LoadWarning.SaveEntityIndex"/> に対象の永続 ID が入る。
        /// </summary>
        PrefabMigrated = 6,

        /// <summary>
        /// ロード失敗時に SubScene の状態がロールバックされた。
        /// <see cref="SubSceneDefinition.WithSceneStateRollback"/> 指定時のみ。
        /// </summary>
        SceneStateRolledBack = 7,

        /// <summary>
        /// FixedString フィールドが容量ぴったりに達しており、文字境界で切れている疑いがある。
        /// </summary>
        FixedStringMayBeTruncated = 8,

        /// <summary>
        /// PrefabMigration が登録されているが、対象 Prefab の GUID が
        /// <see cref="PrefabSchemaRegistry"/> に登録されていない。
        /// <see cref="LoadWarning.ExtraHash"/> に未登録の Prefab GUID が入る。
        /// </summary>
        PrefabSchemaNotRegistered = 9,

        /// <summary>
        /// <see cref="DeletionPolicy.ByScope"/> + <see cref="RestorationScope.EntireWorld"/> で
        /// 大量の Entity が削除された（破壊的操作の注意喚起）。
        /// <see cref="LoadWarning.ExtraHash"/> に削除された Entity 数が入る。
        /// </summary>
        ByScopeDeletionWarning = 10,

        /// <summary>
        /// Prefab のスキーマハッシュが変化しているが、マイグレーションチェーンが見つからない。
        /// 対象 Entity は元の Prefab 構成のまま残る。
        /// <see cref="LoadWarning.SaveEntityIndex"/> に対象の永続 ID が入る。
        /// </summary>
        PrefabSchemaMigrationFailed = 11,

        /// <summary>
        /// ChunkComponent セクションに現れた pid のうち、ロード後の World に対応する
        /// Entity が存在しないものがあった。
        ///
        /// <para>
        /// <b>通常は発火しない</b>: <c>P3_EntityGenerationPhase</c> は ChunkComponent セクションの
        /// 全メンバー pid を Entity 生成対象に含めるため、通常ケースでは
        /// <c>missingEntityCount == 0</c> となり、本警告は発火しない。
        /// </para>
        /// <para>
        /// <b>発生条件</b>: 何らかの異常で Chunk メンバーの復元に失敗した場合
        /// （セーブデータの破損、pid 範囲外、Provider の異常など）に限る。
        /// </para>
        /// <para>
        /// <b>意味</b>: 発火時は「Chunk の値が正しく復元されていない可能性」を示す
        /// 回帰検出用の警告。
        /// </para>
        /// <para>
        /// <see cref="LoadWarning.SaveEntityIndex"/> に最初の未解決 pid、
        /// <see cref="LoadWarning.ExtraHash"/> に未解決件数、
        /// <see cref="LoadWarning.TypeName"/> に ChunkComponent の型名が入る。
        /// </para>
        /// </summary>
        ChunkComponentEntityMissing = 12,
    }
}
