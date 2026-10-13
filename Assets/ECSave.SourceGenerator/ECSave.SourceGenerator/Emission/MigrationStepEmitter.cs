using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Helpers;

namespace ECSave.SourceGenerator.Emission
{
    internal static class MigrationStepEmitter
    {
        public const string MigrationsNamespace = "ECSave.Generated.Migrations";

        public static string Emit(
            string migrationTypeFullName,
            string oldTypeFullName,
            string newTypeFullName,
            TypePlan oldPlan,
            TypePlan newPlan,
            ulong newTypeNameHash,
            ulong oldSchemaHash,
            ulong newSchemaHash)
        {
            var className = TypeNameSanitizer.ToShortIdentifier(migrationTypeFullName) + "Step";

            var w = new IndentedWriter();

            w.Open("namespace " + MigrationsNamespace);
            w.Open($"internal sealed class {className} : global::ECSave.Internal.MigrationStepBase");

            w.Line($"private readonly global::{migrationTypeFullName} _impl = new global::{migrationTypeFullName}();");
            w.Line();

            w.Line($"public override ulong NewTypeNameHash => 0x{newTypeNameHash:X16}UL;");
            w.Line($"public override ulong OldSchemaHash => 0x{oldSchemaHash:X16}UL;");
            w.Line($"public override ulong NewSchemaHash => 0x{newSchemaHash:X16}UL;");
            w.Line();

            w.Open("public override void Migrate(");
            w.Line("    ref global::ECSave.Format.ByteReader reader,");
            w.Line("    ref global::ECSave.Format.ByteWriter writer)");

            w.Line($"var old = default(global::{oldTypeFullName});");
            EmitReadFields(w, oldPlan);
            w.Line();

            w.Line("var @new = _impl.Migrate(in old);");
            w.Line();

            EmitWriteFields(w, newPlan);

            w.Close();
            w.Close();
            w.Close();

            return w.ToString();
        }

        private static void EmitReadFields(IndentedWriter w, TypePlan plan)
        {
            if (plan == null || plan.Fields == null) return;

            for (int i = 0; i < plan.Fields.Count; i++)
            {
                var f = plan.Fields[i];
                if (f.IsEntityRef) continue;
                var read = MapReadMethod(f.ElementKind);
                if (read == null) continue;
                w.Line($"old.{f.Name} = {read};");
            }
        }

        private static void EmitWriteFields(IndentedWriter w, TypePlan plan)
        {
            if (plan == null || plan.Fields == null) return;

            for (int i = 0; i < plan.Fields.Count; i++)
            {
                var f = plan.Fields[i];
                if (f.IsEntityRef) continue;
                var write = MapWriteMethod(f.ElementKind);
                if (write == null) continue;
                w.Line(write.Replace("{v}", "@new." + f.Name) + ";");
            }
        }

        private static string MapReadMethod(SchemaHashKind kind)
        {
            switch (kind)
            {
                case SchemaHashKind.Bool: return "reader.ReadU8() != 0";
                case SchemaHashKind.Int8: return "(sbyte)reader.ReadU8()";
                case SchemaHashKind.Int16: return "(short)reader.ReadU16()";
                case SchemaHashKind.Int32: return "reader.ReadI32()";
                case SchemaHashKind.Int64: return "(long)reader.ReadU64()";
                case SchemaHashKind.UInt8: return "reader.ReadU8()";
                case SchemaHashKind.UInt16: return "reader.ReadU16()";
                case SchemaHashKind.UInt32: return "reader.ReadU32()";
                case SchemaHashKind.UInt64: return "reader.ReadU64()";
                case SchemaHashKind.Float32:
                    return "global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<uint, float>(reader.ReadU32())";
                case SchemaHashKind.Float64:
                    return "global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<ulong, double>(reader.ReadU64())";
                default: return null;
            }
        }

        private static string MapWriteMethod(SchemaHashKind kind)
        {
            switch (kind)
            {
                case SchemaHashKind.Bool: return "writer.WriteU8((byte)({v} ? 1 : 0))";
                case SchemaHashKind.Int8: return "writer.WriteU8((byte){v})";
                case SchemaHashKind.Int16: return "writer.WriteU16((ushort){v})";
                case SchemaHashKind.Int32: return "writer.WriteI32({v})";
                case SchemaHashKind.Int64: return "writer.WriteU64((ulong){v})";
                case SchemaHashKind.UInt8: return "writer.WriteU8({v})";
                case SchemaHashKind.UInt16: return "writer.WriteU16({v})";
                case SchemaHashKind.UInt32: return "writer.WriteU32({v})";
                case SchemaHashKind.UInt64: return "writer.WriteU64({v})";
                case SchemaHashKind.Float32:
                    return "writer.WriteU32(global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<float, uint>({v}))";
                case SchemaHashKind.Float64:
                    return "writer.WriteU64(global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<double, ulong>({v}))";
                default: return null;
            }
        }
    }
}
