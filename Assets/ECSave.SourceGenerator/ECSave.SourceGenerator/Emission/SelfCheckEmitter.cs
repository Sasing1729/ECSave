using ECSave.SourceGenerator.Helpers;

namespace ECSave.SourceGenerator.Emission
{
    internal static class SelfCheckEmitter
    {
        public const string Namespace = "ECSave.Generated.SelfCheck";

        public static string Emit(string typeFullName, int expectedSize)
        {
            var className = TypeNameSanitizer.ToShortIdentifier(typeFullName) + "_ECSaveSelfCheck";
            var escapedType = TypeNameSanitizer.EscapeStringLiteral(typeFullName);

            var w = new IndentedWriter();

            w.Open("namespace " + Namespace);
            w.Open($"internal static class {className}");

            w.Line($"public const string TypeName = \"{escapedType}\";");
            w.Line($"public const int ExpectedSize = {expectedSize};");
            w.Line();

            w.Open("public static int ActualSize()");
            w.Line($"return global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<global::{typeFullName}>();");
            w.Close();
            w.Line();

            w.Open("public static bool Verify()");
            w.Line("return ActualSize() == ExpectedSize;");
            w.Close();

            w.Close();
            w.Close();

            return w.ToString();
        }
    }
}
