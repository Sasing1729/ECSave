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
        /// <see cref="LoadWarning.TypeName"/> には（判明していれば）セクション名が入る。
        /// </summary>
        UnknownSectionIgnored = 1,

        /// <summary>
        /// チャンクコンポーネントの「マージ」が発生したことを示す。
        ///
        /// <para>
        /// <b>発生条件</b>: 保存時に異なるチャンク（Archetype）に属していた Entity が、
        /// ロード後の World では同じチャンクに属する場合。
        /// 例えば保存時に「PlayerOnlyFilter を持つ Entity」と「持たない Entity」が
        /// 別チャンクにあったが、ロード後の World では両方が同じ Archetype になった場合。
        /// </para>
        ///
        /// <para>
        /// <b>データへの影響</b>: データ自体は失われないが、チャンクコンポーネントは
        /// 1 チャンクに 1 つの値しか持てないため、複数グループの値のうち
        /// 「最多メンバーを持つグループ」の値で代表される。他のグループの値は破棄される。
        /// </para>
        ///
        /// <para>
        /// <b>対処</b>: 元のチャンク構成を保持したい場合は、ロード前に World の
        /// Archetype 構成を保存時と同じにする（例: フィルタコンポーネントの付与状態を揃える）。
        /// </para>
        ///
        /// <para>
        /// <see cref="LoadWarning.TypeName"/> にはチャンクコンポーネントの型名が入る。
        /// <see cref="LoadWarning.SaveEntityIndex"/> は <c>-1</c>、
        /// <see cref="LoadWarning.SourceEntity"/> は <c>Entity.Null</c>。
        /// </para>
        /// </summary>
        ChunkComponentMerged = 2,

        /// <summary>
        /// SubScene のロード状態が保存時と現在で一致しない。
        /// <see cref="LoadWarningKind"/> の将来拡張用（Phase S1a 以降）。
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
        /// <see cref="SaveDefinition.AutoRepairFixedString"/> が true の場合、
        /// 自動的に有効な UTF-8 境界まで切り詰められる。
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
    }
}
