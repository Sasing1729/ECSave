using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Discovery
{
    internal sealed class ScopeDescriptor : IEquatable<ScopeDescriptor>
    {
        public string FullTypeName { get; }
        public string NamespaceName { get; }
        public string ClassName { get; }
        public bool IsAbstract { get; }
        public bool ConfigureFound { get; }
        public IReadOnlyList<RegistrationRecord> Registrations { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public ScopeDescriptor(
            string fullTypeName,
            string namespaceName,
            string className,
            bool isAbstract,
            bool configureFound,
            IReadOnlyList<RegistrationRecord> registrations,
            IReadOnlyList<Diagnostic> diagnostics = null)
        {
            FullTypeName = fullTypeName ?? string.Empty;
            NamespaceName = namespaceName ?? string.Empty;
            ClassName = className ?? string.Empty;
            IsAbstract = isAbstract;
            ConfigureFound = configureFound;
            Registrations = registrations ?? Array.Empty<RegistrationRecord>();
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
        }

        public bool Equals(ScopeDescriptor other)
        {
            if (other == null) return false;
            if (FullTypeName != other.FullTypeName) return false;
            if (IsAbstract != other.IsAbstract) return false;
            if (ConfigureFound != other.ConfigureFound) return false;
            if (Registrations.Count != other.Registrations.Count) return false;
            for (int i = 0; i < Registrations.Count; i++)
                if (!Registrations[i].Equals(other.Registrations[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as ScopeDescriptor);

        public override int GetHashCode()
            => (FullTypeName?.GetHashCode() ?? 0)
             ^ IsAbstract.GetHashCode()
             ^ ConfigureFound.GetHashCode();
    }
}
