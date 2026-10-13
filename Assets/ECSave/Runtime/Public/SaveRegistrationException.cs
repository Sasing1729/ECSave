using System;

namespace ECSave
{
    public sealed class SaveRegistrationException : Exception
    {
        public string Code { get; }

        public SaveRegistrationException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
