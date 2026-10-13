using System;
using System.Collections.Generic;

namespace ECSave.SourceGenerator.Discovery
{
    internal sealed class FilterInfo : IEquatable<FilterInfo>
    {
        public IReadOnlyList<string> WithAll { get; }
        public IReadOnlyList<string> WithAny { get; }
        public IReadOnlyList<string> WithNone { get; }

        public static readonly FilterInfo Empty = new FilterInfo(
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        public FilterInfo(
            IReadOnlyList<string> withAll,
            IReadOnlyList<string> withAny,
            IReadOnlyList<string> withNone)
        {
            WithAll = withAll ?? Array.Empty<string>();
            WithAny = withAny ?? Array.Empty<string>();
            WithNone = withNone ?? Array.Empty<string>();
        }

        public bool IsEmpty
            => WithAll.Count == 0 && WithAny.Count == 0 && WithNone.Count == 0;

        public bool Equals(FilterInfo other)
        {
            if (other == null) return false;
            if (WithAll.Count != other.WithAll.Count) return false;
            if (WithAny.Count != other.WithAny.Count) return false;
            if (WithNone.Count != other.WithNone.Count) return false;
            for (int i = 0; i < WithAll.Count; i++)
                if (WithAll[i] != other.WithAll[i]) return false;
            for (int i = 0; i < WithAny.Count; i++)
                if (WithAny[i] != other.WithAny[i]) return false;
            for (int i = 0; i < WithNone.Count; i++)
                if (WithNone[i] != other.WithNone[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as FilterInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < WithAll.Count; i++) h = h * 31 + WithAll[i].GetHashCode();
                for (int i = 0; i < WithAny.Count; i++) h = h * 31 + WithAny[i].GetHashCode();
                for (int i = 0; i < WithNone.Count; i++) h = h * 31 + WithNone[i].GetHashCode();
                return h;
            }
        }
    }
}
