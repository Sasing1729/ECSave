using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ECSave.Internal;

namespace ECSave.Tests.Runtime
{
    public class PlayerLoopCompletionNotifierTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayerLoopCompletionNotifier.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerLoopCompletionNotifier.ResetForTests();
        }

        [UnityTest]
        public IEnumerator Install_FiresOnNextFrames()
        {
            int count = 0;
            var notifier = new PlayerLoopCompletionNotifier();
            notifier.Install(() => count++);

            try
            {
                yield return null;
                yield return null;

                int c1 = count;
                Assert.Greater(c1, 0, "notifier should have fired by now");

                yield return null;
                int c2 = count;
                Assert.Greater(c2, c1);

                yield return null;
                int c3 = count;
                Assert.Greater(c3, c2);
            }
            finally
            {
                notifier.Uninstall();
            }
        }

        [UnityTest]
        public IEnumerator Uninstall_StopsFiring()
        {
            int count = 0;
            var notifier = new PlayerLoopCompletionNotifier();
            notifier.Install(() => count++);

            yield return null;
            yield return null;

            int beforeUninstall = count;
            Assert.Greater(beforeUninstall, 0);

            notifier.Uninstall();

            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(beforeUninstall, count);
        }

        [Test]
        public void Install_NullAction_Throws()
        {
            var notifier = new PlayerLoopCompletionNotifier();
            Assert.Throws<System.ArgumentNullException>(() => notifier.Install(null));
        }

        [Test]
        public void Install_Twice_Throws()
        {
            var notifier = new PlayerLoopCompletionNotifier();
            notifier.Install(() => { });
            try
            {
                Assert.Throws<System.InvalidOperationException>(
                    () => notifier.Install(() => { }));
            }
            finally
            {
                notifier.Uninstall();
            }
        }

        [Test]
        public void Uninstall_NotInstalled_NoOp()
        {
            var notifier = new PlayerLoopCompletionNotifier();
            Assert.DoesNotThrow(() => notifier.Uninstall());
        }

        [UnityTest]
        public IEnumerator MultipleNotifiers_AllFire()
        {
            int countA = 0, countB = 0;
            var n1 = new PlayerLoopCompletionNotifier();
            var n2 = new PlayerLoopCompletionNotifier();
            n1.Install(() => countA++);
            n2.Install(() => countB++);

            try
            {
                yield return null;
                yield return null;

                Assert.Greater(countA, 0);
                Assert.Greater(countB, 0);
            }
            finally
            {
                n1.Uninstall();
                n2.Uninstall();
            }
        }
    }
}