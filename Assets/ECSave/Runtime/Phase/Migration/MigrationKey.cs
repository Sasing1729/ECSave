// Runtime/Phase/Migration/MigrationKey.cs
using System;

namespace ECSave.Internal
{
    /// <summary>
    /// Migration 表のキー（11.1節）。(新型の完全修飾名ハッシュ, 旧 SchemaHash)。
    /// </summary>
    public readonly struct MigrationKey : IEquatable<MigrationKey>
    {
        public readonly ulong NewTypeNameHash;
        public readonly ulong OldSchemaHash;

        public MigrationKey(ulong newTypeNameHash, ulong oldSchemaHash)
        {
            NewTypeNameHash = newTypeNameHash;
            OldSchemaHash = oldSchemaHash;
        }

        public bool Equals(MigrationKey other)
            => NewTypeNameHash == other.NewTypeNameHash && OldSchemaHash == other.OldSchemaHash;

        public override bool Equals(object obj)
            => obj is MigrationKey k && Equals(k);

        public override int GetHashCode()
            => unchecked((int)(NewTypeNameHash ^ OldSchemaHash));
    }
}
