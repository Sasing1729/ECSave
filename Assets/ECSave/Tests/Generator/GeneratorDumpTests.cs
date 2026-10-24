using System;
using System.Linq;
using NUnit.Framework;

namespace ECSave.Tests.Generator
{
    /// <summary>
    /// 生成型の存在確認をダンプする診断用テスト。CI からは除外する。
    /// </summary>
    [TestFixture]
    [Category("Verification")]
    public class GeneratorDumpTests
    {
        [Test]
        public void Diagnostic_CheckGeneratedTypes()
        {
            var names = new[]
            {
                "ECSave.Generated.GeneratorContractVersion",
                "ECSave.Generated.ECSaveScopeRegistry",
                "ECSave.Generated.Modules.GenHealthModule",
                "ECSave.Generated.Modules.GenStunnedModule",
                "ECSave.Generated.Bindings.GenTestScope_ECSaveBindings",
                "ECSave.Generated.SelfCheck.GenHealth_ECSaveSelfCheck",
            };

            TestContext.WriteLine("=== Generated type presence ===");
            foreach (var n in names)
            {
                bool found = false;
                foreach (var a in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
                {
                    try
                    {
                        var t = a.GetType(n, throwOnError: false);
                        if (t != null)
                        {
                            TestContext.WriteLine($"  {n}  →  {a.GetName().Name}");
                            found = true;
                            break;
                        }
                    }
                    catch { }
                }
                if (!found) TestContext.WriteLine($"  {n}  →  NOT FOUND");
            }

            TestContext.WriteLine();
            TestContext.WriteLine("=== All types under ECSave.Generated.* ===");
            foreach (var a in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                Type[] types;
                try { types = a.GetTypes(); }
                catch { continue; }

                foreach (var t in types.Where(t =>
                    t.Namespace != null &&
                    t.Namespace.StartsWith("ECSave.Generated")))
                {
                    TestContext.WriteLine($"  {a.GetName().Name} :: {t.FullName}");
                }
            }

            Assert.Pass("See TestContext output");
        }
    }
}
