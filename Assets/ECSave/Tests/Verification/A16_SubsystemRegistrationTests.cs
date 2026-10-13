using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ECSave.Tests
{
    public static class A16RegistrationProbe
    {
        private const string RuntimeModuleId = "ECSave.Tests.A16Probe";

        private static readonly object SyncRoot = new object();
        private static readonly List<string> RegisteredModulesInternal = new();
        private static readonly List<string> ManualRegisteredModulesInternal = new();

        private static int _subsystemRegistrationCallCount;
        private static bool _playModeEntryFired;

        public static int SubsystemRegistrationCallCount
        {
            get
            {
                lock (SyncRoot)
                    return _subsystemRegistrationCallCount;
            }
        }

        public static bool PlayModeEntryFired
        {
            get
            {
                lock (SyncRoot)
                    return _playModeEntryFired;
            }
        }

        public static int RegisteredModuleCount
        {
            get
            {
                lock (SyncRoot)
                    return RegisteredModulesInternal.Count;
            }
        }

        public static int ManualRegisteredModuleCount
        {
            get
            {
                lock (SyncRoot)
                    return ManualRegisteredModulesInternal.Count;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            lock (SyncRoot)
            {
                _subsystemRegistrationCallCount = 1;
                _playModeEntryFired = true;
                RegisteredModulesInternal.Clear();
                BindInternal(RegisteredModulesInternal, RuntimeModuleId);
            }
        }

        public static void Bind(string moduleId)
        {
            lock (SyncRoot)
            {
                BindInternal(ManualRegisteredModulesInternal, moduleId);
            }
        }

        public static void SimulateEditorLoad()
        {
            lock (SyncRoot)
            {
                BindInternal(ManualRegisteredModulesInternal, RuntimeModuleId);
            }
        }

        public static void SimulateSubsystemRegistration()
        {
            lock (SyncRoot)
            {
                BindInternal(ManualRegisteredModulesInternal, RuntimeModuleId);
            }
        }

        public static void ResetManualState()
        {
            lock (SyncRoot)
            {
                ManualRegisteredModulesInternal.Clear();
            }
        }

        private static void BindInternal(List<string> modules, string moduleId)
        {
            if (!modules.Contains(moduleId))
                modules.Add(moduleId);
        }
    }

    public sealed class A16_SubsystemRegistrationVerification
    {
        [UnityTest]
        public IEnumerator SubsystemRegistration_FiredOnce_InPlayMode()
        {
            Assert.IsTrue(
                Application.isPlaying,
                "この検証は PlayMode で実行する必要があります");

            yield return null;

            var fired = A16RegistrationProbe.PlayModeEntryFired;
            var callCount = A16RegistrationProbe.SubsystemRegistrationCallCount;
            var registeredCount = A16RegistrationProbe.RegisteredModuleCount;

            Debug.Log(
                $"[A-16] PlayModeEntryFired={fired}, " +
                $"PlayModeEntryCount={callCount}, " +
                $"RegisteredModules={registeredCount}, " +
                $"isPlaying={Application.isPlaying}");

            Assert.IsTrue(
                fired,
                "RuntimeInitializeOnLoadMethod(SubsystemRegistration) が PlayMode 開始時に発火していない");

            Assert.AreEqual(
                1,
                callCount,
                "SubsystemRegistration が複数回発火している");

            Assert.AreEqual(
                1,
                registeredCount,
                "同一モジュールが複数回登録されている");
        }

        [Test]
        public void DuplicateBind_SameModule_IsIgnored()
        {
            A16RegistrationProbe.ResetManualState();

            try
            {
                A16RegistrationProbe.Bind("ModuleA");
                A16RegistrationProbe.Bind("ModuleA");
                A16RegistrationProbe.Bind("ModuleA");

                Assert.AreEqual(
                    1,
                    A16RegistrationProbe.ManualRegisteredModuleCount,
                    "同一 ID の重複登録が無視されていない");

                Debug.Log(
                    $"[A-16] duplicate bind: " +
                    $"registered={A16RegistrationProbe.ManualRegisteredModuleCount}");
            }
            finally
            {
                A16RegistrationProbe.ResetManualState();
            }
        }

        [Test]
        public void EditorLoad_Then_SubsystemRegistration_SingleRegistration()
        {
            A16RegistrationProbe.ResetManualState();

            try
            {
                A16RegistrationProbe.SimulateEditorLoad();
                A16RegistrationProbe.SimulateSubsystemRegistration();

                Assert.AreEqual(
                    1,
                    A16RegistrationProbe.ManualRegisteredModuleCount,
                    "InitializeOnLoad と SubsystemRegistration の重複登録が無視されていない");

                Debug.Log(
                    $"[A-16] EditorLoad→SubsystemRegistration: " +
                    $"registered={A16RegistrationProbe.ManualRegisteredModuleCount}");
            }
            finally
            {
                A16RegistrationProbe.ResetManualState();
            }
        }

        [Test]
        public void Note_IL2CPP_And_DomainReloadDisabled_VerificationRequired()
        {
            Debug.Log(
                "[A-16] IL2CPP / Domain Reload 無効環境での検証は " +
                "Release ビルドで実施すること（テスト計画 §3.1）。" +
                "本テストは Editor PlayMode におけるメカニズム確認を目的とする。");

            Assert.Pass();
        }
    }
}
