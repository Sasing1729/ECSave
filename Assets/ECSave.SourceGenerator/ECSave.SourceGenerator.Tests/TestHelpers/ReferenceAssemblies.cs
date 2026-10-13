using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Tests.TestHelpers
{
    internal static class ReferenceAssemblies
    {
        private static MetadataReference[] _cached;

        public static MetadataReference[] Get()
        {
            if (_cached != null) return _cached;

            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.RuntimeHelpers).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            };

            var trusted = Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty,
                "System.Runtime.dll");
            if (File.Exists(trusted))
                refs.Add(MetadataReference.CreateFromFile(trusted));

            _cached = refs.ToArray();
            return _cached;
        }
    }
}
