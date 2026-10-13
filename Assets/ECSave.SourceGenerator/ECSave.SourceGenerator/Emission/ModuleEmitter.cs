using System.Collections.Generic;
using System.Globalization;
using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Helpers;

namespace ECSave.SourceGenerator.Emission
{
    internal static class ModuleEmitter
    {
        public const string ModulesNamespace = "ECSave.Generated.Modules";

        private const string PlainBase = "SaveTypeModulePlain";
        private const string EnableableBase = "SaveTypeModuleEnableable";

        public static string Emit(
            string typeFullName,
            string baseClassName,
            TypePlan plan,
            byte[] schemaHashInput,
            ulong schemaHash,
            FilterInfo filter,
            bool withoutEnableableState)
        {
            var className = TypeNameSanitizer.ToShortIdentifier(typeFullName) + "Module";
            var escapedType = TypeNameSanitizer.EscapeStringLiteral(typeFullName);

            var w = new IndentedWriter();

            w.Open("namespace " + ModulesNamespace);
            w.Open($"internal sealed class {className} : " +
                   $"global::ECSave.Internal.{baseClassName}<global::{typeFullName}>");

            w.Line($"public const string TypeName = \"{escapedType}\";");
            w.Line();

            EmitSchemaHashInput(w, schemaHashInput);
            w.Line();

            w.Line($"private const ulong s_schemaHash = 0x{schemaHash:X16}UL;");
            w.Line();

            EmitFieldNames(w, plan);
            w.Line();

            EmitEntityRefOffsets(w, plan);

            w.Line("public override ulong SchemaHash => s_schemaHash;");
            w.Line("public override string FullTypeName => TypeName;");
            w.Line("public override byte[] SchemaHashInput => s_schemaHashInput;");
            w.Line("public override string[] FieldNames => s_fieldNames;");

            if (withoutEnableableState && baseClassName == EnableableBase)
            {
                w.Line("public override bool HasEnabledBits => false;");
            }

            w.Line();

            EmitBuildQuery(w, typeFullName, filter);

            if (RequiresWriteValue(baseClassName))
                EmitWriteValue(w, typeFullName, plan);

            w.Close();
            w.Close();

            return w.ToString();
        }

        public static bool RequiresWriteValue(string baseClassName)
            => baseClassName == PlainBase || baseClassName == EnableableBase;

        private static void EmitSchemaHashInput(IndentedWriter w, byte[] input)
        {
            w.Line("private static readonly byte[] s_schemaHashInput = new byte[]");
            w.Open(null);

            if (input == null || input.Length == 0)
            {
                w.Close(";");
                return;
            }

            for (int i = 0; i < input.Length; i += 12)
            {
                var parts = new List<string>(12);
                int end = i + 12;
                if (end > input.Length) end = input.Length;
                for (int j = i; j < end; j++)
                    parts.Add("0x" + input[j].ToString("X2", CultureInfo.InvariantCulture));

                w.Line(string.Join(", ", parts) + ",");
            }

            w.Close(";");
        }

        private static void EmitFieldNames(IndentedWriter w, TypePlan plan)
        {
            w.Line("private static readonly string[] s_fieldNames = new string[]");
            w.Open(null);

            if (plan != null && plan.Fields != null)
            {
                for (int i = 0; i < plan.Fields.Count; i++)
                {
                    var name = TypeNameSanitizer.EscapeStringLiteral(plan.Fields[i].Name);
                    w.Line($"\"{name}\",");
                }
            }

            w.Close(";");
        }

        private static void EmitEntityRefOffsets(IndentedWriter w, TypePlan plan)
        {
            if (plan == null || plan.EntityRefOffsets == null || plan.EntityRefOffsets.Length == 0)
                return;

            w.Line("private static readonly int[] s_entityRefOffsets = new int[]");
            w.Open(null);
            for (int i = 0; i < plan.EntityRefOffsets.Length; i++)
                w.Line(plan.EntityRefOffsets[i].ToString(CultureInfo.InvariantCulture) + ",");
            w.Close(";");
            w.Line();
            w.Line("public override int[] EntityRefOffsets => s_entityRefOffsets;");
            w.Line();
        }

        private static void EmitBuildQuery(
            IndentedWriter w,
            string typeFullName,
            FilterInfo filter)
        {
            if (filter == null || filter.IsEmpty) return;

            w.Open("public override global::Unity.Entities.EntityQuery BuildQuery(" +
                   "global::Unity.Entities.EntityManager em)");
            w.Line("var builder = new global::Unity.Entities.EntityQueryBuilder(" +
                   "global::Unity.Collections.Allocator.Temp);");
            w.Line($"builder.WithAll<global::{typeFullName}>();");

            for (int i = 0; i < filter.WithAll.Count; i++)
                w.Line($"builder.WithAll<global::{filter.WithAll[i]}>();");

            if (filter.WithAny.Count > 0)
            {
                for (int i = 0; i < filter.WithAny.Count; i++)
                {
                    if (i == 0)
                        w.Line($"builder.WithAny<global::{filter.WithAny[i]}>()");
                    else
                        w.Line($"    .WithAny<global::{filter.WithAny[i]}>()");

                    if (i == filter.WithAny.Count - 1)
                        w.Line(";");
                }
            }

            for (int i = 0; i < filter.WithNone.Count; i++)
                w.Line($"builder.WithNone<global::{filter.WithNone[i]}>();");

            w.Line("builder.WithOptions(global::Unity.Entities.EntityQueryOptions.IgnoreComponentEnabledState);");
            w.Line("return builder.Build(em);");
            w.Close();
            w.Line();
        }

        private static void EmitWriteValue(
            IndentedWriter w,
            string typeFullName,
            TypePlan plan)
        {
            w.Line("protected override unsafe void WriteValue(");
            w.Line("    global::Unity.Entities.Entity entity,");
            w.Line("    global::Unity.Entities.EntityManager em,");
            w.Line("    byte* value)");
            w.Open(null);

            int structSize = plan != null ? plan.StructSize : 0;

            if (structSize <= 0)
            {
                w.Line($"if (!em.HasComponent<global::{typeFullName}>(entity))");
                w.Indent();
                w.Line($"em.AddComponentData(entity, default(global::{typeFullName}));");
                w.Outdent();
            }
            else
            {
                w.Line($"var v = default(global::{typeFullName});");
                w.Line("global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(");
                w.Line("    global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref v),");
                w.Line("    value,");
                w.Line($"    global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<global::{typeFullName}>());");
                w.Line($"if (em.HasComponent<global::{typeFullName}>(entity))");
                w.Indent();
                w.Line("em.SetComponentData(entity, v);");
                w.Outdent();
                w.Line("else");
                w.Indent();
                w.Line("em.AddComponentData(entity, v);");
                w.Outdent();
            }

            w.Close();
        }
    }
}
