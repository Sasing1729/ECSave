using ECSave.Format;

namespace ECSave.Internal
{
    /// <summary>
    /// Component 単位 Migration の抽象基底（11.1節）。
    /// 具象クラスは Source Generator が生成する（Phase 3）。
    /// Managed ループで実行するため Burst 不可（11.2節）。
    /// </summary>
    public abstract class MigrationStepBase
    {
        /// <summary>
        /// この Migration が対象とする新型の完全修飾名ハッシュ（11.1節）。
        /// MigrationTable のキーの第1要素。
        /// </summary>
        public abstract ulong NewTypeNameHash { get; }

        /// <summary>このステップが要求する旧レイアウトの SchemaHash。</summary>
        public abstract ulong OldSchemaHash { get; }

        /// <summary>このステップが生成する新レイアウトの SchemaHash。</summary>
        public abstract ulong NewSchemaHash { get; }

        /// <summary>
        /// 1 レコードを変換する。旧 Reader から読み、新 Writer に書く（11.2節）。
        /// </summary>
        public abstract void Migrate(ref ByteReader reader, ref ByteWriter writer);
    }
}
