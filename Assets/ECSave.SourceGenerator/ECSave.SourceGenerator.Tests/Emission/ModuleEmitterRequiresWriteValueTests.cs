using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class ModuleEmitterRequiresWriteValueTests
    {
        [TestCase("SaveTypeModulePlain", true)]
        [TestCase("SaveTypeModuleEnableable", true)]
        [TestCase("SaveTypeModuleShared", false)]
        [TestCase("SaveTypeModuleBuffer", false)]
        [TestCase("SaveTypeModuleBufferEnableable", false)]
        [TestCase("SaveTypeModuleChunk", false)]
        public void RequiresWriteValue_ReturnsExpected(string baseClassName, bool expected)
        {
            Assert.AreEqual(expected, ModuleEmitter.RequiresWriteValue(baseClassName));
        }
    }
}
