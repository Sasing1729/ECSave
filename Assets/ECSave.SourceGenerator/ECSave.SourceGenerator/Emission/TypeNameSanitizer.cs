using System.Text;

namespace ECSave.SourceGenerator.Emission
{
    internal static class TypeNameSanitizer
    {
        public static string ToIdentifier(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "_";

            var sb = new StringBuilder(fullName.Length + 8);
            for (int i = 0; i < fullName.Length; i++)
            {
                char c = fullName[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        public static string ToShortIdentifier(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "_";

            int lastDot = fullName.LastIndexOf('.');
            string shortName = lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;

            int lastPlus = shortName.LastIndexOf('+');
            if (lastPlus >= 0) shortName = shortName.Substring(lastPlus + 1);

            int lastTick = shortName.IndexOf('`');
            if (lastTick >= 0) shortName = shortName.Substring(0, lastTick);

            return ToIdentifier(shortName);
        }

        public static string EscapeStringLiteral(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
