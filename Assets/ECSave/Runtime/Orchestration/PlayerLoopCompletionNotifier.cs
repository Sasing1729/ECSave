using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ECSave.Internal
{
    public sealed class PlayerLoopCompletionNotifier
    {
        private static readonly List<PlayerLoopCompletionNotifier> s_active = new();
        private static bool s_installed;

        private Action _moveNext;

        public bool IsActive { get; private set; }

        public void Install(Action moveNext)
        {
            if (moveNext == null) throw new ArgumentNullException(nameof(moveNext));
            if (IsActive) throw new InvalidOperationException("Already installed");

            _moveNext = moveNext;
            IsActive = true;
            s_active.Add(this);
            EnsureInstalled();
        }

        public void Uninstall()
        {
            if (!IsActive) return;
            IsActive = false;
            s_active.Remove(this);
            _moveNext = null;
        }

        private static void EnsureInstalled()
        {
            if (s_installed) return;
            s_installed = true;

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
            if (idx < 0) return;

            var post = loop.subSystemList[idx];
            var oldSubs = post.subSystemList ?? Array.Empty<PlayerLoopSystem>();

            var notifier = new PlayerLoopSystem
            {
                type = typeof(PlayerLoopCompletionNotifier),
                updateDelegate = Tick,
            };

            var newSubs = new PlayerLoopSystem[oldSubs.Length + 1];
            Array.Copy(oldSubs, newSubs, oldSubs.Length);
            newSubs[oldSubs.Length] = notifier;
            post.subSystemList = newSubs;
            loop.subSystemList[idx] = post;
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void Tick()
        {
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                var n = s_active[i];
                if (!n.IsActive) continue;
                try { n._moveNext?.Invoke(); }
                catch { }
            }
        }

        /// <summary>
        /// Review #11: Domain Reload 無効時（Enter Play Mode Options）の
        /// 静的状態リセット。PlayerLoop は Play モード終了時に Unity が再構築するが、
        /// <c>s_installed</c>（managed static）は残るため、2 回目以降の Play で
        /// Install が発火しなくなる。
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ResetOnLoad()
        {
            s_installed = false;
            s_active.Clear();
        }

        internal static void ResetForTests()
        {
            s_active.Clear();
            s_installed = false;
        }
    }
}
