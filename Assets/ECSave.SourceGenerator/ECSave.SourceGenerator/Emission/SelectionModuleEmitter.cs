using ECSave.SourceGenerator.Helpers;

namespace ECSave.SourceGenerator.Emission
{
    internal static class SelectionModuleEmitter
    {
        public const string SelectionsNamespace = "ECSave.Generated.Selections";

        public static string Emit(
            string typeFullName,
            string fieldPath,
            bool isBuffer)
        {
            var className = TypeNameSanitizer.ToShortIdentifier(typeFullName)
                + "_"
                + TypeNameSanitizer.ToIdentifier(fieldPath)
                + "SelectionModule";

            var escapedType = TypeNameSanitizer.EscapeStringLiteral(typeFullName);
            var escapedField = TypeNameSanitizer.EscapeStringLiteral(fieldPath);

            var w = new IndentedWriter();

            w.Open("namespace " + SelectionsNamespace);
            w.Open($"internal sealed class {className} : global::ECSave.Internal.SelectionModule");

            w.Line($"public override string FullTypeName => \"{escapedType}\";");
            w.Line($"public override string FieldPath => \"{escapedField}\";");
            w.Line();

            EmitBuildQuery(w, typeFullName, isBuffer);
            w.Line();
            EmitExtractSelected(w, typeFullName, fieldPath, isBuffer);

            w.Close();
            w.Close();

            return w.ToString();
        }

        private static void EmitBuildQuery(IndentedWriter w, string typeFullName, bool isBuffer)
        {
            var readOnly = isBuffer
                ? "global::Unity.Entities.ComponentType.ReadOnly<global::" + typeFullName + ">()"
                : "global::Unity.Entities.ComponentType.ReadOnly<global::" + typeFullName + ">()";

            w.Open("public override global::Unity.Entities.EntityQuery BuildQuery(" +
                   "global::Unity.Entities.EntityManager em)");
            w.Line("return em.CreateEntityQuery(new global::Unity.Entities.EntityQueryDesc");
            w.Open(null);
            w.Line("All = new global::Unity.Entities.ComponentType[]");
            w.Open(null);
            w.Line(readOnly + ",");
            w.Close(",");
            w.Line("Options = global::Unity.Entities.EntityQueryOptions.IgnoreComponentEnabledState,");
            w.Close(");");
            w.Close();
        }

        private static void EmitExtractSelected(
            IndentedWriter w,
            string typeFullName,
            string fieldPath,
            bool isBuffer)
        {
            w.Open("public override unsafe void ExtractSelected(" +
                   "global::Unity.Entities.EntityQuery query, " +
                   "global::Unity.Entities.EntityManager em, " +
                   "global::Unity.Collections.NativeParallelHashSet<global::Unity.Entities.Entity> selected)");

            if (isBuffer)
            {
                w.Line("var bufferHandle = em.GetBufferTypeHandle<global::" + typeFullName + ">(isReadOnly: true);");
                w.Line("using var chunks = query.ToArchetypeChunkArray(global::Unity.Collections.Allocator.TempJob);");
                w.Line();
                w.Line("for (int c = 0; c < chunks.Length; c++)");
                w.Open(null);
                w.Line("var accessor = chunks[c].GetBufferAccessor(ref bufferHandle);");
                w.Line("for (int i = 0; i < accessor.Length; i++)");
                w.Open(null);
                w.Line("var buf = accessor[i];");
                w.Line("for (int k = 0; k < buf.Length; k++)");
                w.Open(null);
                w.Line($"var target = buf[k].{fieldPath};");
                w.Line("if (target != global::Unity.Entities.Entity.Null)");
                w.Indent();
                w.Line("selected.Add(target);");
                w.Outdent();
                w.Close();
                w.Close();
                w.Close();
            }
            else
            {
                w.Line("using var entities = query.ToEntityArray(global::Unity.Collections.Allocator.TempJob);");
                w.Line($"using var values = query.ToComponentDataArray<global::{typeFullName}>(global::Unity.Collections.Allocator.TempJob);");
                w.Line();
                w.Line("for (int i = 0; i < entities.Length; i++)");
                w.Open(null);
                w.Line($"var target = values[i].{fieldPath};");
                w.Line("if (target != global::Unity.Entities.Entity.Null)");
                w.Indent();
                w.Line("selected.Add(target);");
                w.Outdent();
                w.Close();
            }

            w.Close();
        }
    }
}
