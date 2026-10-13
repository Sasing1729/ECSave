using System.Text;

namespace ECSave.SourceGenerator.Helpers
{
    internal sealed class IndentedWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(1024);
        private int _indent;

        public IndentedWriter Indent()
        {
            _indent++;
            return this;
        }

        public IndentedWriter Outdent()
        {
            if (_indent > 0) _indent--;
            return this;
        }

        public IndentedWriter Line()
        {
            _sb.AppendLine();
            return this;
        }

        public IndentedWriter Line(string text)
        {
            for (int i = 0; i < _indent; i++) _sb.Append("    ");
            _sb.AppendLine(text);
            return this;
        }

        public IndentedWriter Raw(string text)
        {
            _sb.Append(text);
            return this;
        }

        public IndentedWriter Open(string text)
        {
            if (!string.IsNullOrEmpty(text)) Line(text);
            Line("{");
            Indent();
            return this;
        }

        public IndentedWriter Close()
        {
            return Close(null);
        }

        public IndentedWriter Close(string suffix)
        {
            Outdent();
            Line(suffix == null ? "}" : "}" + suffix);
            return this;
        }

        public override string ToString() => _sb.ToString();
    }
}
