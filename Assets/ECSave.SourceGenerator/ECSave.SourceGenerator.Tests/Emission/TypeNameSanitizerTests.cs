using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class TypeNameSanitizerTests
    {
        [Test]
        public void ToIdentifier_ReplacesDotsAndPlus()
        {
            Assert.AreEqual("Game_Domain_Health",
                TypeNameSanitizer.ToIdentifier("Game.Domain.Health"));
        }

        [Test]
        public void ToIdentifier_ReplacesAngleBrackets()
        {
            Assert.AreEqual("List_Int32_",
                TypeNameSanitizer.ToIdentifier("List<Int32>"));
        }

        [Test]
        public void ToIdentifier_Empty_ReturnsUnderscore()
        {
            Assert.AreEqual("_", TypeNameSanitizer.ToIdentifier(""));
        }

        [Test]
        public void ToIdentifier_PreservesLettersDigitsUnderscore()
        {
            Assert.AreEqual("My_Type_42",
                TypeNameSanitizer.ToIdentifier("My_Type_42"));
        }

        [Test]
        public void EscapeStringLiteral_QuotesAndBackslash()
        {
            var escaped = TypeNameSanitizer.EscapeStringLiteral("a\"b\\c");
            Assert.AreEqual("a\\\"b\\\\c", escaped);
        }

        [Test]
        public void EscapeStringLiteral_NewlineAndTab()
        {
            var escaped = TypeNameSanitizer.EscapeStringLiteral("a\nb\tc");
            Assert.AreEqual("a\\nb\\tc", escaped);
        }
    }
}
