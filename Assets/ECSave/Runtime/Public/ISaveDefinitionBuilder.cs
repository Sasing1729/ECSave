using System;
using Unity.Entities;

namespace ECSave
{
    public interface ISaveDefinitionBuilder
    {
        SaveDefinitionBuilder<T> RegisterComponent<T>() where T : unmanaged, IComponentData;
        SaveDefinitionBuilder<T> RegisterBuffer<T>() where T : unmanaged, IBufferElementData;
        SaveDefinitionBuilder<T> RegisterSharedComponent<T>() where T : unmanaged, ISharedComponentData;
        SaveDefinitionBuilder<T> RegisterChunkComponent<T>() where T : unmanaged, IComponentData;
        void RegisterIdentity<T>() where T : unmanaged, IComponentData;
        void RegisterMigration<TMigration>() where TMigration : class, new();
        void SelectEntityReference<T>(Func<T, Entity> field) where T : unmanaged, IComponentData;
        void SelectEntityReferences<T>(Func<T, Entity> field) where T : unmanaged, IBufferElementData;
        void GameDataVersion(int version);
        void WorldId(string id);

        /// <summary>
        /// FixedString の自動修復（P7.2）を無効化する。
        /// 無効化した場合、Deserialize 時に切詰め疑いを検出しても警告のみで、
        /// データは変更しない。デフォルトは有効（自動修復する）。
        /// </summary>
        void DisableAutoRepairFixedString();

        // ── PidMigration サポート（Phase 1）──────────────────

        /// <summary>
        /// <typeparamref name="TSave"/> の Pid 形 <typeparamref name="TPid"/> を登録する。
        ///
        /// <para>
        /// <b>Pid 形</b>とは、<typeparamref name="TSave"/> の <c>Entity</c> フィールドを
        /// <c>uint</c>（pid）に置換した型である。<typeparamref name="TSave"/> が
        /// <c>Entity</c> フィールドを含む場合、通常の <see cref="RegisterMigration{TMigration}"/>
        /// （= <see cref="IComponentMigration{TOld,TNew}"/>）では移行できない
        /// （D6 フェーズで Entity が未解決のため）。Pid 形を経由することで、
        /// Entity を含む型でもスキーマ進化が可能になる。
        /// </para>
        /// <para>
        /// <b>必須の制約</b>: <typeparamref name="TPid"/> のサイズ（<c>StructSize</c>）は
        /// <typeparamref name="TSave"/> の <c>RecordStride</c>（data stride）と一致し、
        /// 各フィールドの名前・型・オフセットが保存型と対応している必要がある。
        /// これらの整合性は Source Generator がコンパイル時に検証する
        /// （<c>ECSAVE051</c> / <c>ECSAVE052</c>）。
        /// </para>
        /// <para>
        /// <b>登録の副作用</b>: このメソッドは実行時には何もしない。Source Generator が
        /// コンパイル時に PidForm を検出し、対応する <c>IPidMigration</c> 実装型の
        /// MigrationStep を生成する。
        /// </para>
        /// </summary>
        void RegisterPidForm<TSave, TPid>()
            where TSave : unmanaged
            where TPid : unmanaged;

        /// <summary>
        /// <see cref="IPidMigration{TOldPid, TNewPid}"/> 実装型を登録する。
        ///
        /// <para>
        /// <b>前提</b>: <c>TOldPid</c> / <c>TNewPid</c> に対応する PidForm が
        /// <see cref="RegisterPidForm{TSave, TPid}"/> で登録されていること。
        /// 未登録の場合、Source Generator が <c>ECSAVE053</c> を発行する。
        /// </para>
        /// <para>
        /// <b>登録の副作用</b>: このメソッドは実行時には何もしない。Source Generator が
        /// コンパイル時に PidMigration を検出し、<c>IPidMigrationStep</c> を生成する。
        /// </para>
        /// </summary>
        void RegisterPidMigration<TMigration>() where TMigration : class, new();
    }
}
