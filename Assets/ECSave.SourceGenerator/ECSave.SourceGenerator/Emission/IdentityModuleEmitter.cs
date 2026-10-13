using System.Collections.Generic;
using System.Globalization;
using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Helpers;

namespace ECSave.SourceGenerator.Emission
{
    internal static class IdentityModuleEmitter
    {
        public const string ModulesNamespace = "ECSave.Generated.Modules";

        public static string Emit(
            string typeFullName,
            TypePlan plan,
            byte[] schemaHashInput,
            ulong schemaHash)
        {
            var className = TypeNameSanitizer.ToShortIdentifier(typeFullName) + "IdentityModule";
            var escapedType = TypeNameSanitizer.EscapeStringLiteral(typeFullName);

            var w = new IndentedWriter();

            w.Open("namespace " + ModulesNamespace);
            w.Open($"internal sealed class {className} : " +
                   $"global::ECSave.Internal.IdentityModule");

            w.Line($"public const string TypeName = \"{escapedType}\";");
            w.Line();

            EmitSchemaHashInput(w, schemaHashInput);
            w.Line();

            w.Line($"private const ulong s_schemaHash = 0x{schemaHash:X16}UL;");
            w.Line();

            EmitFieldNames(w, plan);
            w.Line();

            int keyStride = plan != null ? plan.StructSize : 0;

            w.Line("public override ulong SchemaHash => s_schemaHash;");
            w.Line("public override string FullTypeName => TypeName;");
            w.Line("public override int KeyStride => " + keyStride + ";");
            w.Line("public override byte[] SchemaHashInput => s_schemaHashInput;");
            w.Line("public override string[] FieldNames => s_fieldNames;");
            w.Line();

            EmitBuildQuery(w, typeFullName);
            w.Line();
            EmitComputeBodySize(w, keyStride);
            w.Line();
            EmitWriteSectionBody(w, typeFullName);
            w.Line();
            EmitBuildWorldIndex(w, typeFullName);
            w.Line();
            EmitBuildSaveIndex(w, keyStride);
            w.Line();
            EmitApplySectionBody(w, typeFullName);

            w.Close();
            w.Close();

            return w.ToString();
        }

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

        private static void EmitBuildQuery(IndentedWriter w, string typeFullName)
        {
            w.Open("public override global::Unity.Entities.EntityQuery BuildQuery(" +
                   "global::Unity.Entities.EntityManager em)");
            w.Line("return em.CreateEntityQuery(new global::Unity.Entities.EntityQueryDesc");
            w.Open(null);
            w.Line("All = new global::Unity.Entities.ComponentType[]");
            w.Open(null);
            w.Line($"global::Unity.Entities.ComponentType.ReadOnly<global::{typeFullName}>(),");
            w.Close(",");
            w.Line("Options = global::Unity.Entities.EntityQueryOptions.IgnoreComponentEnabledState,");
            w.Close(");");
            w.Close();
        }

        private static void EmitComputeBodySize(IndentedWriter w, int keyStride)
        {
            w.Open("public override int ComputeBodySize(" +
                   "global::Unity.Entities.EntityManager em, " +
                   "global::Unity.Collections.NativeParallelHashMap<global::Unity.Entities.Entity, int> idOf)");
            w.Line("var q = BuildQuery(em);");
            w.Line("using var entities = q.ToEntityArray(global::Unity.Collections.Allocator.TempJob);");
            w.Line("int count = 0;");
            w.Line("for (int i = 0; i < entities.Length; i++)");
            w.Indent();
            w.Line("if (idOf.ContainsKey(entities[i])) count++;");
            w.Outdent();
            w.Line($"return global::ECSave.Format.CommonSectionWriter.ComputeFixedBodySize(");
            w.Line($"    count, {keyStride}, hasEnabledBits: false);");
            w.Close();
        }

        private static void EmitWriteSectionBody(IndentedWriter w, string typeFullName)
        {
            w.Open("public override unsafe void WriteSectionBody(" +
                   "ref global::ECSave.Format.FormatWriter writer, " +
                   "global::Unity.Entities.EntityManager em, " +
                   "global::Unity.Collections.NativeParallelHashMap<global::Unity.Entities.Entity, int> idOf)");
            w.Line("var q = BuildQuery(em);");
            w.Line("using var entities = q.ToEntityArray(global::Unity.Collections.Allocator.TempJob);");
            w.Line($"using var values = q.ToComponentDataArray<global::{typeFullName}>(global::Unity.Collections.Allocator.TempJob);");
            w.Line();
            w.Line("int count = 0;");
            w.Line("for (int i = 0; i < entities.Length; i++)");
            w.Indent();
            w.Line("if (idOf.ContainsKey(entities[i])) count++;");
            w.Outdent();
            w.Line();
            w.Line("writer.WriteU32((uint)count);");
            w.Line("writer.WriteU32(global::ECSave.Format.ECSaveFormatConstants.SectionFlags_FixedLength);");
            w.Line("writer.WriteU32((uint)KeyStride);");
            w.Line();
            w.Line("if (count > 0)");
            w.Open(null);
            w.Line("using var indices = new global::Unity.Collections.NativeArray<uint>(count, global::Unity.Collections.Allocator.TempJob);");
            w.Line("uint* idst = (uint*)global::Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(indices);");
            w.Line("int j = 0;");
            w.Line("for (int i = 0; i < entities.Length; i++)");
            w.Indent();
            w.Line("if (idOf.ContainsKey(entities[i])) idst[j++] = (uint)idOf[entities[i]];");
            w.Outdent();
            w.Line("writer.WriteBytes((byte*)idst, count * 4);");
            w.Close();
            w.Line();
            w.Line("if (KeyStride > 0 && count > 0)");
            w.Open(null);
            w.Line("using var records = new global::Unity.Collections.NativeArray<byte>(count * KeyStride, global::Unity.Collections.Allocator.TempJob);");
            w.Line("byte* rptr = (byte*)global::Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(records);");
            w.Line("byte* vsrc = (byte*)global::Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values);");
            w.Line("int k = 0;");
            w.Line("for (int i = 0; i < entities.Length; i++)");
            w.Open(null);
            w.Line("if (!idOf.ContainsKey(entities[i])) continue;");
            w.Line("for (int b = 0; b < KeyStride; b++)");
            w.Indent();
            w.Line("rptr[k * KeyStride + b] = vsrc[i * KeyStride + b];");
            w.Outdent();
            w.Line("k++;");
            w.Close();
            w.Line("writer.WriteBytes(rptr, count * KeyStride);");
            w.Close();
            w.Close();
        }

        private static void EmitBuildWorldIndex(IndentedWriter w, string typeFullName)
        {
            w.Open("public override unsafe global::ECSave.SaveLoadErrorCode BuildWorldIndex(" +
                   "global::Unity.Entities.EntityManager em, " +
                   "global::Unity.Entities.EntityQuery query, " +
                   "ref global::ECSave.Internal.IdentityIndex index, " +
                   "uint typeIndex)");
            w.Line("using var entities = query.ToEntityArray(global::Unity.Collections.Allocator.TempJob);");
            w.Line($"using var values = query.ToComponentDataArray<global::{typeFullName}>(global::Unity.Collections.Allocator.TempJob);");
            w.Line();
            w.Line("byte* src = (byte*)global::Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values);");
            w.Line("for (int i = 0; i < entities.Length; i++)");
            w.Open(null);
            w.Line("byte* key = src + i * KeyStride;");
            w.Line("if (index.HasWorldConflict(typeIndex, key, KeyStride))");
            w.Indent();
            w.Line("return global::ECSave.SaveLoadErrorCode.ValidationFailed;");
            w.Outdent();
            w.Line("index.AddWorldEntry(typeIndex, key, KeyStride, entities[i]);");
            w.Close();
            w.Line("return global::ECSave.SaveLoadErrorCode.None;");
            w.Close();
        }

        private static void EmitBuildSaveIndex(IndentedWriter w, int keyStride)
        {
            w.Open("public override unsafe global::ECSave.SaveLoadErrorCode BuildSaveIndex(" +
                   "global::ECSave.Format.ByteView sectionBody, " +
                   "ref global::ECSave.Internal.IdentityIndex index, " +
                   "uint typeIndex)");
            w.Line("var v = global::ECSave.Format.CommonSectionReader.Read(sectionBody);");
            w.Line("int count = (int)v.Header.Count;");
            w.Line();
            w.Line("for (int i = 0; i < count; i++)");
            w.Open(null);
            w.Line("uint pid = v.EntityIndex[i];");
            w.Line("byte* src = v.Records + i * KeyStride;");
            w.Line("if (index.HasSaveConflict(typeIndex, src, KeyStride))");
            w.Indent();
            w.Line("return global::ECSave.SaveLoadErrorCode.ValidationFailed;");
            w.Outdent();
            w.Line("index.AddSaveEntry(typeIndex, src, KeyStride, (int)pid);");
            w.Close();
            w.Line("return global::ECSave.SaveLoadErrorCode.None;");
            w.Close();
        }

        private static void EmitApplySectionBody(IndentedWriter w, string typeFullName)
        {
            w.Open("public override unsafe void ApplySectionBody(" +
                   "global::ECSave.Format.ByteView sectionBody, " +
                   "global::Unity.Collections.NativeArray<global::Unity.Entities.Entity> persistentToEntity, " +
                   "global::Unity.Entities.EntityManager em)");
            w.Line("var v = global::ECSave.Format.CommonSectionReader.Read(sectionBody);");
            w.Line("int count = (int)v.Header.Count;");
            w.Line();
            w.Line("for (int i = 0; i < count; i++)");
            w.Open(null);
            w.Line("uint pid = v.EntityIndex[i];");
            w.Line("if (pid >= persistentToEntity.Length) continue;");
            w.Line("var target = persistentToEntity[(int)pid];");
            w.Line("if (target == global::Unity.Entities.Entity.Null) continue;");
            w.Line();
            w.Line($"var value = default(global::{typeFullName});");
            w.Line("byte* valPtr = (byte*)&value;");
            w.Line("byte* srcPtr = v.Records + i * KeyStride;");
            w.Line("for (int b = 0; b < KeyStride; b++) valPtr[b] = srcPtr[b];");
            w.Line();
            w.Line($"if (em.HasComponent<global::{typeFullName}>(target))");
            w.Indent();
            w.Line("em.SetComponentData(target, value);");
            w.Outdent();
            w.Line("else");
            w.Indent();
            w.Line("em.AddComponentData(target, value);");
            w.Outdent();
            w.Close();
            w.Close();
        }
    }
}
