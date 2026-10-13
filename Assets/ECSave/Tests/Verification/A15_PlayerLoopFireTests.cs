using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.TestTools;

namespace ECSave.Tests
{
    public class A15_PlayerLoopFireTests
    {
        private struct FireCounter
        {
            public int Count;
            public int LastFrame;
            public double LastTime;
        }

        private class FireSystem
        {
            public FireCounter Counter;
            public PlayerLoopSystem System;

            public void Update()
            {
                Counter.Count++;
                Counter.LastFrame = Time.frameCount;
                Counter.LastTime = Time.realtimeSinceStartupAsDouble;
            }
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_Injected_FiresNextFrame()
        {
            var state = new FireSystem();
            var ps = new PlayerLoopSystem
            {
                type = typeof(FireSystem),
                updateDelegate = state.Update,
            };
            state.System = ps;

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            int idx = -1;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == typeof(PostLateUpdate))
                {
                    idx = i;
                    break;
                }
            }
            Assert.GreaterOrEqual(idx, 0, "PostLateUpdate must exist");

            var post = loop.subSystemList[idx];
            var oldSubs = post.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            var newSubs = new PlayerLoopSystem[oldSubs.Length + 1];
            Array.Copy(oldSubs, newSubs, oldSubs.Length);
            newSubs[oldSubs.Length] = ps;
            post.subSystemList = newSubs;
            loop.subSystemList[idx] = post;

            PlayerLoop.SetPlayerLoop(loop);
            try
            {
                int startFrame = Time.frameCount;
                int startCount = state.Counter.Count;

                yield return null;
                yield return null;
                yield return null;

                Assert.Greater(state.Counter.Count, startCount,
                    "PlayerLoopSystem should fire at least once after injection");
                Assert.Greater(state.Counter.LastFrame, startFrame,
                    "Last fire should be on a frame after injection");
            }
            finally
            {
                var restore = PlayerLoop.GetCurrentPlayerLoop();
                if (idx < restore.subSystemList.Length)
                {
                    var postR = restore.subSystemList[idx];
                    postR.subSystemList = oldSubs;
                    restore.subSystemList[idx] = postR;
                    PlayerLoop.SetPlayerLoop(restore);
                }
            }
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_Injected_FiresEveryFrame()
        {
            var state = new FireSystem();
            var ps = new PlayerLoopSystem
            {
                type = typeof(FireSystem),
                updateDelegate = state.Update,
            };

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            int idx = Array.FindIndex(loop.subSystemList,
                s => s.type == typeof(PostLateUpdate));
            Assert.GreaterOrEqual(idx, 0);

            var post = loop.subSystemList[idx];
            var oldSubs = post.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            var newSubs = new PlayerLoopSystem[oldSubs.Length + 1];
            Array.Copy(oldSubs, newSubs, oldSubs.Length);
            newSubs[oldSubs.Length] = ps;
            post.subSystemList = newSubs;
            loop.subSystemList[idx] = post;

            PlayerLoop.SetPlayerLoop(loop);
            try
            {
                yield return null;
                yield return null;

                int c1 = state.Counter.Count;
                Assert.Greater(c1, 0, "PostLateUpdate should have fired by now");

                yield return null;
                int c2 = state.Counter.Count;
                Assert.Greater(c2, c1, "should fire every frame (1st)");

                yield return null;
                int c3 = state.Counter.Count;
                Assert.Greater(c3, c2, "should fire every frame (2nd)");
            }
            finally
            {
                var restore = PlayerLoop.GetCurrentPlayerLoop();
                if (idx < restore.subSystemList.Length)
                {
                    var postR = restore.subSystemList[idx];
                    postR.subSystemList = oldSubs;
                    restore.subSystemList[idx] = postR;
                    PlayerLoop.SetPlayerLoop(restore);
                }
            }
        }
    }
}
