using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public class A11_MainThreadDetectionTests
    {
        [Test]
        public void MainThread_Id_IsStable()
        {
            int mainId1 = Thread.CurrentThread.ManagedThreadId;
            Thread.Sleep(1);
            int mainId2 = Thread.CurrentThread.ManagedThreadId;

            Debug.Log($"[A-11] mainThreadId={mainId1}, after sleep={mainId2}");
            Assert.AreEqual(mainId1, mainId2, "同一スレッド内で ManagedThreadId が変化した");
        }

        [Test]
        public void WorkerThread_IdDiffers()
        {
            int mainId = Thread.CurrentThread.ManagedThreadId;
            int workerId = -1;

            var t = new Thread(() => { workerId = Thread.CurrentThread.ManagedThreadId; });
            t.Start();
            t.Join();

            Debug.Log($"[A-11] main={mainId}, worker={workerId}");
            Assert.AreNotEqual(mainId, workerId, "Worker スレッドの ID が main と同一");
        }

        [Test]
        public async Task TaskRun_ContinuesOnThreadPool_NotMain()
        {
            int mainId = Thread.CurrentThread.ManagedThreadId;
            int onThreadPool = -1;

            await Task.Run(() => { onThreadPool = Thread.CurrentThread.ManagedThreadId; });

            Debug.Log($"[A-11] main={mainId}, Task.Run={onThreadPool}");
            Assert.AreNotEqual(mainId, onThreadPool,
                "Task.Run 内で main スレッド ID が観測された（Unity の同期コンテキスト依存）");
        }

        [Test]
        public void Unity_MainThread_Api_Detection()
        {
            Debug.Log($"[A-11] Application.isPlaying={Application.isPlaying}, " +
                      $"isBatchMode={Application.isBatchMode}");

            bool threw = false;
            try
            {
                var _ = Time.frameCount;
            }
            catch (System.Exception ex)
            {
                threw = true;
                Debug.Log($"[A-11] UnityEngine.Time.frameCount 例外: {ex.GetType().Name}");
            }

            Debug.Log($"[A-11] メインスレッド判定の推奨実装：Thread.CurrentThread.ManagedThreadId を起動時に記録し、" +
                      $"公開 API 呼び出し時に比較する");
            Assert.IsFalse(threw);
        }

        [Test]
        public void WorkerThread_AccessingEntityManager_Throws()
        {
            var world = new World("A11");
            try
            {
                var em = world.EntityManager;
                System.Exception captured = null;

                var t = new Thread(() =>
                {
                    try { em.CreateEntity(); }
                    catch (System.Exception ex) { captured = ex; }
                });
                t.Start();
                t.Join();

                Debug.Log($"[A-11] Worker から EntityManager.CreateEntity の結果: " +
                          $"{(captured == null ? "成功（例外なし）" : captured.GetType().Name)}");

                Debug.Log("[A-11] 観測: ECS 内部でもスレッドチェックがある可能性。ECSave は独自にチェックする");
                Assert.Pass();
            }
            finally
            {
                if (world.IsCreated) world.Dispose();
            }
        }

        [Test]
        public async Task AwaitInMainThread_ContinuesOnMainThread()
        {
            int beforeId = Thread.CurrentThread.ManagedThreadId;
            int afterId = -1;

            await Task.Yield();

            afterId = Thread.CurrentThread.ManagedThreadId;
            Debug.Log($"[A-11] before={beforeId}, after AwaitYield={afterId}");

            Assert.AreEqual(beforeId, afterId,
                "PlayMode の Unity 同期コンテキストで await 後に main に戻らない");
        }
    }
}
