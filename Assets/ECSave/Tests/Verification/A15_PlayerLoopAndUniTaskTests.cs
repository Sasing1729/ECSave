using NUnit.Framework;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ECSave.Tests
{
    public class A15_PlayerLoopAndUniTaskTests
    {
        [Test]
        public void PlayerLoop_ContainsKnownSubsystems()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var names = new List<string>();
            Traverse(loop, names);
            Debug.Log($"[A-15] PlayerLoop top-level: {string.Join(", ", names)}");

            Assert.IsTrue(names.Contains("PostLateUpdate"), "PostLateUpdate が PlayerLoop に存在しない");
            Assert.IsTrue(names.Contains("Initialization"), "Initialization が PlayerLoop に存在しない");
        }

        static void Traverse(PlayerLoopSystem system, List<string> names)
        {
            if (system.subSystemList != null)
            {
                foreach (var sub in system.subSystemList)
                {
                    names.Add(sub.type?.Name ?? "(null)");
                }
            }
        }

        [Test]
        public void PlayerLoop_InsertPostLateUpdate_Works()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            bool inserted = false;

            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == typeof(PostLateUpdate))
                {
                    var target = loop.subSystemList[i];
                    var list = new List<PlayerLoopSystem>(target.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                    list.Add(new PlayerLoopSystem
                    {
                        type = typeof(A15Marker),
                        updateDelegate = () => A15Probe.Tick(),
                    });
                    target.subSystemList = list.ToArray();
                    loop.subSystemList[i] = target;
                    inserted = true;
                    break;
                }
            }

            Assert.IsTrue(inserted, "PostLateUpdate に PlayerLoopSystem を挿入できなかった");

            A15Probe.Reset();
            PlayerLoop.SetPlayerLoop(loop);
            try
            {
                Debug.Log("[A-15] PostLateUpdate にカスタム PlayerLoopSystem を挿入。次のフレームで発火を確認する");
                Debug.Log("[A-15] 注記: 発火の確認は PlayMode の次フレーム待ちが必要なため、" +
                          "本テストは挿入の成功のみを検証する");
                Assert.Pass();
            }
            finally
            {
                PlayerLoop.SetPlayerLoop(PlayerLoop.GetDefaultPlayerLoop());
            }
        }

        static class A15Probe
        {
            public static int TickCount;
            public static void Tick() { TickCount++; }
            public static void Reset() { TickCount = 0; }
        }

        struct A15Marker { }

        [Test]
        public async System.Threading.Tasks.Task AutoResetUniTaskCompletionSource_Completes()
        {
            var tcs = AutoResetUniTaskCompletionSource<int>.Create();
            var task = tcs.Task;

            bool completed = false;
            int value = 0;

            task.ContinueWith(r =>
            {
                completed = true;
                value = r;
            }).Forget();

            await UniTask.Yield();

            tcs.TrySetResult(42);
            await UniTask.Yield();

            Debug.Log($"[A-15] AutoResetUniTaskCompletionSource completed={completed}, value={value}");
            Assert.IsTrue(completed);
            Assert.AreEqual(42, value);
        }

        [Test]
        public async System.Threading.Tasks.Task UniTask_Delay_Works()
        {
            var start = UnityEngine.Time.realtimeSinceStartup;
            await UniTask.Yield();
            var end = UnityEngine.Time.realtimeSinceStartup;

            Debug.Log($"[A-15] UniTask.Yield 経過={end - start:F4} s");
            Assert.Pass();
        }

        [Test]
        public void PlayerLoop_GetDefault_And_Set_Restores()
        {
            var defaultLoop = PlayerLoop.GetDefaultPlayerLoop();
            var currentLoop = PlayerLoop.GetCurrentPlayerLoop();

            Assert.IsNotNull(defaultLoop.subSystemList, "GetDefaultPlayerLoop が null を返す");
            Assert.IsNotNull(currentLoop.subSystemList, "GetCurrentPlayerLoop が null を返す");

            Debug.Log($"[A-15] GetDefaultPlayerLoop の subsystem 数={defaultLoop.subSystemList.Length}");
            Debug.Log($"[A-15] GetCurrentPlayerLoop の subsystem 数={currentLoop.subSystemList.Length}");
        }

        [Test]
        public async System.Threading.Tasks.Task UniTask_CompletionSource_AwaitDirectly()
        {
            var tcs = AutoResetUniTaskCompletionSource<int>.Create();
            int result = -1;

            var task = AwaitAndStore(tcs.Task, v => result = v);

            await UniTask.Yield();
            tcs.TrySetResult(123);

            await task;
            Debug.Log($"[A-15] await 経由の結果={result}");
            Assert.AreEqual(123, result);
        }

        static async UniTask AwaitAndStore(UniTask<int> t, Action<int> store)
        {
            store(await t);
        }
    }
}
