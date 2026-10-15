#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.MinimalE2E;
using UnityEngine;

namespace ECSave.Tests.Runtime
{
    public class MigrationBindingWindowTests
    {
        private const string WindowTypeName = "ECSave.EditorTools.MigrationBindingWindow";
        private const string TempScopeName = "ECSave.MigrationBindings.Temp";

        private MigrationTestScope _scope;
        private ScriptableObject _window;
        private Type _windowType;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();

            _windowType = null;
            foreach (var asm in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                _windowType = asm.GetType(WindowTypeName);
                if (_windowType != null) break;
            }
            Assert.IsNotNull(_windowType, "MigrationBindingWindow type not found");

            DestroyAllMigrationTestScopes();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null) UnityEngine.Object.DestroyImmediate(_window);
            if (_scope != null) UnityEngine.Object.DestroyImmediate(_scope);
            _window = null;
            _scope = null;
            DestroyAllMigrationTestScopes();
        }

        private static void DestroyAllMigrationTestScopes()
        {
            var all = Resources.FindObjectsOfTypeAll<MigrationTestScope>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null) UnityEngine.Object.DestroyImmediate(all[i]);
            }
        }

        private static int CountRealScopes()
        {
            int n = 0;
            var all = Resources.FindObjectsOfTypeAll<SaveScope>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].name == TempScopeName) continue;
                n++;
            }
            return n;
        }

        private void CreateScope()
        {
            _scope = ScriptableObject.CreateInstance<MigrationTestScope>();
            _scope.hideFlags = HideFlags.HideAndDontSave;
        }

        private void SetField(string name, object value)
        {
            var f = _windowType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, name + " not found");
            f.SetValue(_window, value);
        }

        private object GetField(string name)
        {
            var f = _windowType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, name + " not found");
            return f.GetValue(_window);
        }

        private void Invoke(string method)
        {
            var m = _windowType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, method + " not found");
            m.Invoke(_window, null);
        }

        private void CreateWindowAndRefresh()
        {
            _window = ScriptableObject.CreateInstance(_windowType);
            SetField("_includeAllTypes", false);
            Invoke("Refresh");
        }

        private bool TryFindEntry(string scopeName, out MigrationEntry entry)
        {
            entry = default;
            var list = (IList)GetField("_entries");
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var t = item.GetType();
                var name = (string)t.GetField("Item1").GetValue(item);
                if (name != scopeName) continue;
                entry = (MigrationEntry)t.GetField("Item2").GetValue(item);
                return true;
            }
            return false;
        }

        [Test]
        public void Refresh_WithMigrationScope_ListsEntry()
        {
            CreateScope();
            CreateWindowAndRefresh();

            Assert.IsTrue(string.IsNullOrEmpty((string)GetField("_errorMessage")),
                "unexpected error: " + (string)GetField("_errorMessage"));

            Assert.IsTrue(TryFindEntry(typeof(MigrationTestScope).FullName, out var entry),
                "entry for MigrationTestScope not listed");

            Assert.AreEqual(SchemaHashCore.HashUtf8("Game.Domain.Health"), entry.NewTypeNameHash);
            Assert.AreEqual(HealthV1Module.Hash, entry.OldSchemaHash);
            Assert.AreEqual(new HealthModule().SchemaHash, entry.NewSchemaHash);
        }

        [Test]
        public void Refresh_AfterScopeDestroyed_EntryDisappears()
        {
            CreateScope();
            CreateWindowAndRefresh();
            Assert.IsTrue(TryFindEntry(typeof(MigrationTestScope).FullName, out _));

            UnityEngine.Object.DestroyImmediate(_scope);
            _scope = null;

            Invoke("Refresh");
            Assert.IsFalse(TryFindEntry(typeof(MigrationTestScope).FullName, out _));
        }

        [Test]
        public void Refresh_NoScopeAtAll_ShowsErrorMessage()
        {
            if (CountRealScopes() > 0)
                Assert.Ignore("real SaveScope instances exist in the editor");

            CreateWindowAndRefresh();

            var msg = (string)GetField("_errorMessage");
            Assert.IsFalse(string.IsNullOrEmpty(msg));
        }

        [Test]
        public void Refresh_ScopeWithoutMigration_ReportsNoEntryForIt()
        {
            var plain = ScriptableObject.CreateInstance<ECSave.Tests.Generator.GenSecondaryScope>();
            plain.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                CreateWindowAndRefresh();
                Assert.IsFalse(TryFindEntry(
                    typeof(ECSave.Tests.Generator.GenSecondaryScope).FullName, out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void Refresh_RenamedList_IsEmptyForBuilderRegisteredScope()
        {
            CreateScope();
            CreateWindowAndRefresh();

            var renames = (IList)GetField("_renames");
            int own = 0;
            for (int i = 0; i < renames.Count; i++)
            {
                var item = renames[i];
                var name = (string)item.GetType().GetField("Item1").GetValue(item);
                if (name == typeof(MigrationTestScope).FullName) own++;
            }
            Assert.AreEqual(0, own);
        }
    }
}
#endif