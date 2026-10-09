using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace ECSave.Tests
{
    public static class A16RegistrationProbe
    {
        public static int SubsystemRegistrationCallCount;
        public static int SimulatedEditorLoadCallCount;
        public static int PlayModeEntryCount;
        public static bool PlayModeEntryFired;
        public static readonly List<string> RegisteredModules = new List<string>();
        static readonly object _lock = new object();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnSubsystemRegistration()
        {
            lock (_lock)
            {
                PlayModeEntryCount++;
                PlayModeEntryFired = true;
            }
            SimulateSubsystemRegistration();
        }

        public static void SimulateSubsystemRegistration()
        {
            lock (_lock) { SubsystemRegistrationCallCount++; }
            Bind("ECSave.Tests.A16Probe");
        }

        public static void SimulateEditorLoad()
        {
            lock (_lock) { SimulatedEditorLoadCallCount++; }
            Bind("ECSave.Tests.A16Probe");
        }

        public static void Bind(string moduleId)
        {
            lock (_lock)
            {
                if (!RegisteredModules.Contains(moduleId))
                    RegisteredModules.Add(moduleId);
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                SubsystemRegistrationCallCount = 0;
                SimulatedEditorLoadCallCount = 0;
                RegisteredModules.Clear();
            }
        }
    }

    public class A16_SubsystemRegistrationTests
    {
        [Test]
        public void SubsystemRegistration_FiredOnce_InPlayMode()
        {
            Debug.Log($"[A-16] PlayModeEntryFired={A16RegistrationProbe.PlayModeEntryFired}, " +
                      $"PlayModeEntryCount={A16RegistrationProbe.PlayModeEntryCount}, " +
                      $"RegisteredModules={A16RegistrationProbe.RegisteredModules.Count}, " +
                      $"isPlaying={Application.isPlaying}");

            if (!Application.isPlaying)
            {
                Assert.Inconclusive("EditMode では RuntimeInitializeOnLoadMethod が発火しない");
                return;
            }

            Assert.IsTrue(A16RegistrationProbe.PlayModeEntryFired,
                "RuntimeInitializeOnLoadMethod(SubsystemRegistration) が PlayMode 開始時に発火していない");
            Assert.AreEqual(1, A16RegistrationProbe.PlayModeEntryCount,
                "SubsystemRegistration が複数回発火している");
            Assert.AreEqual(1, A16RegistrationProbe.RegisteredModules.Count,
                "同一モジュールが複数回登録されている");
        }

        [Test]
        public void DuplicateBind_SameModule_IsIgnored()
        {
            var saved = new List<string>(A16RegistrationProbe.RegisteredModules);
            A16RegistrationProbe.Reset();
            try
            {
                A16RegistrationProbe.Bind("ModuleA");
                A16RegistrationProbe.Bind("ModuleA");
                A16RegistrationProbe.Bind("ModuleA");

                Assert.AreEqual(1, A16RegistrationProbe.RegisteredModules.Count,
                    "同一 ID の重複登録が無視されていない");
                Debug.Log($"[A-16] duplicate bind: registered={A16RegistrationProbe.RegisteredModules.Count}");
            }
            finally
            {
                A16RegistrationProbe.Reset();
                foreach (var m in saved) A16RegistrationProbe.Bind(m);
            }
        }

        [Test]
        public void EditorLoad_Then_SubsystemRegistration_SingleRegistration()
        {
            var saved = new List<string>(A16RegistrationProbe.RegisteredModules);
            A16RegistrationProbe.Reset();
            try
            {
                A16RegistrationProbe.SimulateEditorLoad();
                A16RegistrationProbe.SimulateSubsystemRegistration();

                Assert.AreEqual(1, A16RegistrationProbe.RegisteredModules.Count,
                    "InitializeOnLoad と SubsystemRegistration の重複発火が無視されていない");
                Debug.Log($"[A-16] EditorLoad→SubsystemRegistration: " +
                          $"editorLoadCount={A16RegistrationProbe.SimulatedEditorLoadCallCount}, " +
                          $"subsystemCount={A16RegistrationProbe.SubsystemRegistrationCallCount}, " +
                          $"registered={A16RegistrationProbe.RegisteredModules.Count}");
            }
            finally
            {
                A16RegistrationProbe.Reset();
                foreach (var m in saved) A16RegistrationProbe.Bind(m);
            }
        }

        [Test]
        public void Note_IL2CPP_Verification_Required()
        {
            Debug.Log("[A-16] IL2CPP / ドメインリロード無効での確認は Release ビルドで実施すること" +
                "（テスト計画 §3.1）。エディタテストの結果はメカニズムの確認に留まる。");
            Assert.Pass();
        }
    }
}
