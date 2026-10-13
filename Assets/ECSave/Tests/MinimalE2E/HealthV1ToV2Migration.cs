using ECSave.Format;
using ECSave.Internal;

namespace Game.MinimalE2E
{
    internal sealed class HealthV1ToV2Migration : MigrationStepBase
    {
        public static readonly ulong NameHash = SchemaHashCore.HashUtf8(HealthV1Module.Full);
        public static readonly ulong OldHash = HealthV1Module.Hash;
        public static readonly ulong NewHash = new HealthModule().SchemaHash;

        public override ulong NewTypeNameHash => NameHash;
        public override ulong OldSchemaHash => OldHash;
        public override ulong NewSchemaHash => NewHash;

        public override void Migrate(ref ByteReader reader, ref ByteWriter writer)
        {
            int v = reader.ReadI32();
            writer.WriteI32(v);
            writer.WriteI32(0);
        }
    }
}
