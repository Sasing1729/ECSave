using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Helpers;

namespace ECSave.SourceGenerator.Emission
{
    internal static class BindingEmitter
    {
        public const string BindingsNamespace = "ECSave.Generated.Bindings";

        public static string Emit(ScopeDescriptor scope)
        {
            if (scope == null) return string.Empty;

            var className = TypeNameSanitizer.ToShortIdentifier(scope.FullTypeName) + "_ECSaveBindings";
            var scopeName = TypeNameSanitizer.EscapeStringLiteral(scope.FullTypeName);

            var w = new IndentedWriter();

            w.Open("namespace " + BindingsNamespace);
            w.Open($"public sealed class {className} : " +
                   $"global::ECSave.Internal.Contract.ISaveModuleBinder");
            w.Line($"public const string ScopeId = \"{scopeName}\";");
            w.Line();
            w.Open("public void BindModules(global::ECSave.ISaveDefinitionBuilder builder)");
            w.Line("var impl = builder as global::ECSave.SaveDefinitionBuilderImpl;");
            w.Line("if (impl == null) return;");

            EmitRegistrationLines(w, scope);

            w.Close();
            w.Close();
            w.Close();

            return w.ToString();
        }

        public static string EmitRegistration(ScopeDescriptor scope)
        {
            if (scope == null) return string.Empty;
            if (scope.IsAbstract) return string.Empty;

            var className = TypeNameSanitizer.ToShortIdentifier(scope.FullTypeName) + "_ECSaveBindings";
            var registrationName = TypeNameSanitizer.ToShortIdentifier(scope.FullTypeName) + "_ECSaveRegistration";
            var scopeName = TypeNameSanitizer.EscapeStringLiteral(scope.FullTypeName);

            var w = new IndentedWriter();

            w.Open("namespace " + BindingsNamespace);
            w.Open($"internal static class {registrationName}");

            w.Line("[global::UnityEngine.RuntimeInitializeOnLoadMethod(" +
                   "global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            w.Open("private static void Register()");
            w.Line($"global::ECSave.Internal.Contract.GeneratedScopeRegistry.Register(");
            w.Line($"    \"{scopeName}\",");
            w.Line($"    new {className}());");
            w.Close();

            w.Close();
            w.Close();

            return w.ToString();
        }

        private static void EmitRegistrationLines(IndentedWriter w, ScopeDescriptor scope)
        {
            for (int i = 0; i < scope.Registrations.Count; i++)
            {
                var r = scope.Registrations[i];
                switch (r.Kind)
                {
                    case RegistrationKind.Component:
                    case RegistrationKind.Buffer:
                    case RegistrationKind.SharedComponent:
                    case RegistrationKind.ChunkComponent:
                        w.Line($"impl.RegisterModule(new global::ECSave.Generated.Modules." +
                               $"{TypeNameSanitizer.ToShortIdentifier(r.TypeArgumentFullName)}Module());");
                        break;
                    case RegistrationKind.Identity:
                        w.Line($"impl.RegisterIdentityModule(new global::ECSave.Generated.Modules." +
                               $"{TypeNameSanitizer.ToShortIdentifier(r.TypeArgumentFullName)}IdentityModule());");
                        break;
                    case RegistrationKind.SelectEntityReference:
                    case RegistrationKind.SelectEntityReferences:
                        if (string.IsNullOrEmpty(r.FieldPath)) break;
                        w.Line($"impl.RegisterSelectionModule(new global::ECSave.Generated.Selections." +
                               $"{TypeNameSanitizer.ToShortIdentifier(r.TypeArgumentFullName)}_" +
                               $"{TypeNameSanitizer.ToIdentifier(r.FieldPath)}SelectionModule());");
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
