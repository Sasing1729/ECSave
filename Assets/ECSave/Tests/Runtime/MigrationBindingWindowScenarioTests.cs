#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.Contract;
using ECSave.Tests.Generator;
using Game.MinimalE2E;
using UnityEngine;

namespace ECSave.Tests.Runtime
{
    public class MigrationBindingWindowScenarioTests
    {
        private const string WindowTypeName = "ECSave.EditorTools.MigrationBindingWindow";

        private static readonly Type[] FixtureTypes =
        {
            typeof(MigrationOkScope),
            typeof(MigrationStaleScope),
            typeof(MigrationOrphanScope),
            typeof(MigrationUnknownScope),
            typeof(MigrationMultiScope),
            typeof(MigrationBrokenScope),
            typeof(GenPadMigrationScope),
        };

        private Type _windowType;
        private Type _rowType;
        private ScriptableObject _window;

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

            _rowType = _windowType.GetNestedType("MigrationRow", BindingFlags.NonPublic);
            Assert.IsNotNull(_rowType, "MigrationRow type not found");

            DestroyFixtureScopes();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null) UnityEngine.Object.DestroyImmediate(_window);
            _window = null;
            DestroyFixtureScopes();
        }

        private static void DestroyFixtureScopes()
        {
            for (int i = 0; i < FixtureTypes.Length; i++)
            {
                var all = Resources.FindObjectsOfTypeAll(FixtureTypes[i]);
                for (int k = 0; k < all.Length; k++)
                {
                    if (all[k] != null) UnityEngine.Object.DestroyImmediate(all[k]);
                }
            }
        }

        private static T Create<T>() where T : SaveScope
        {
            var s = ScriptableObject.CreateInstance<T>();
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static object Field(object target, string name)
        {
            var f = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(f, name + " not found");
            return f.GetValue(target);
        }

        private void SetWindowField(string name, object value)
        {
            var f = _windowType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, name + " not found");
            f.SetValue(_window, value);
        }

        private void Refresh()
        {
            var m = _windowType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Refresh not found");
            m.Invoke(_window, null);
        }

        private void CreateWindowAndRefresh(bool includeAll = false)
        {
            _window = ScriptableObject.CreateInstance(_windowType);
            SetWindowField("_includeAllTypes", includeAll);
            Refresh();
        }

        private void InvokeStatic(string name)
        {
            var m = _windowType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, name + " not found");
            m.Invoke(null, null);
        }

        private object FindScope(string name)
        {
            foreach (var s in (IList)Field(_window, "_scopes"))
            {
                if ((string)Field(s, "Name") == name) return s;
            }
            return null;
        }

        private static IList RowsOf(object scopeView) => (IList)Field(scopeView, "Rows");

        private static string StatusOf(object scopeView, int index = 0)
            => Field(RowsOf(scopeView)[index], "Status").ToString();

        private int CountEntries(string scopeName)
        {
            int n = 0;
            foreach (var item in (IList)Field(_window, "_entries"))
            {
                var name = (string)item.GetType().GetField("Item1").GetValue(item);
                if (name == scopeName) n++;
            }
            return n;
        }

        private bool RowMatches(string typeName, ulong a, ulong b, ulong c, string filter)
        {
            var row = Activator.CreateInstance(_rowType, true);
            _rowType.GetField("Entry").SetValue(row, new MigrationEntry(a, b, c, typeName));
            _rowType.GetField("TypeName").SetValue(row, typeName);

            var m = _windowType.GetMethod("RowMatches", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "RowMatches not found");
            return (bool)m.Invoke(null, new object[] { row, filter });
        }

        [Test]
        public void Status_Ok_WhenSchemaHashMatches()
        {
            var name = typeof(MigrationOkScope).FullName;
            Create<MigrationOkScope>();
            CreateWindowAndRefresh();

            var view = FindScope(name);
            Assert.IsNotNull(view);
            Assert.AreEqual(1, RowsOf(view).Count);
            Assert.AreEqual("Ok", StatusOf(view));

            var row = RowsOf(view)[0];
            Assert.AreEqual("Game.Domain.Health", (string)Field(row, "TypeName"));
            Assert.AreEqual(new HealthModule().SchemaHash, (ulong)Field(row, "CurrentSchemaHash"));
        }

        [Test]
        public void Status_Stale_WhenSchemaHashDiffers()
        {
            var name = typeof(MigrationStaleScope).FullName;
            Create<MigrationStaleScope>();
            CreateWindowAndRefresh();

            var view = FindScope(name);
            Assert.IsNotNull(view);
            Assert.AreEqual("Stale", StatusOf(view));

            var row = RowsOf(view)[0];
            Assert.AreEqual(new HealthModule().SchemaHash, (ulong)Field(row, "CurrentSchemaHash"));
            var entry = (MigrationEntry)Field(row, "Entry");
            Assert.AreEqual(StaleHealthMigration.BadNewHash, entry.NewSchemaHash);
        }

        [Test]
        public void Status_Orphan_WhenTypeNotRegistered()
        {
            var name = typeof(MigrationOrphanScope).FullName;
            Create<MigrationOrphanScope>();
            CreateWindowAndRefresh();

            var view = FindScope(name);
            Assert.IsNotNull(view);
            Assert.AreEqual("Orphan", StatusOf(view));
        }

        [Test]
        public void Status_Unknown_WhenScopeHasNoModulesAndIsUnbound()
        {
            var name = typeof(MigrationUnknownScope).FullName;

            _window = ScriptableObject.CreateInstance(_windowType);
            InvokeStatic("EnsureGeneratedBindings");

            var snapshot = GeneratedScopeRegistry.Snapshot();
            try
            {
                GeneratedScopeRegistry.Remove(name);
                Create<MigrationUnknownScope>();

                SetWindowField("_includeAllTypes", false);
                Refresh();

                var view = FindScope(name);
                Assert.IsNotNull(view);
                Assert.IsFalse((bool)Field(view, "Bound"));
                Assert.AreEqual(0, (int)Field(view, "ModuleCount"));
                Assert.AreEqual("Unknown", StatusOf(view));
            }
            finally
            {
                GeneratedScopeRegistry.Restore(snapshot);
            }
        }

        [Test]
        public void Status_Ok_ForGeneratedStep_WithGeneratedTypeName()
        {
            var name = typeof(GenPadMigrationScope).FullName;
            Create<GenPadMigrationScope>();
            CreateWindowAndRefresh();

            var view = FindScope(name);
            Assert.IsNotNull(view);
            Assert.AreEqual(1, RowsOf(view).Count);
            Assert.AreEqual("Ok", StatusOf(view));
            Assert.AreEqual(typeof(GenPadV2).FullName,
                (string)Field(RowsOf(view)[0], "TypeName"));
        }

        [Test]
        public void Rows_MultipleMigrations_AllListed()
        {
            var name = typeof(MigrationMultiScope).FullName;
            Create<MigrationMultiScope>();
            CreateWindowAndRefresh();

            var view = FindScope(name);
            Assert.IsNotNull(view);
            Assert.AreEqual(2, RowsOf(view).Count);
            Assert.AreEqual("Ok", StatusOf(view, 0));
            Assert.AreEqual("Ok", StatusOf(view, 1));
            Assert.AreEqual(2, CountEntries(name));
        }

        [Test]
        public void Scopes_AreSortedByFullName()
        {
            Create<MigrationStaleScope>();
            Create<MigrationOkScope>();
            Create<MigrationOrphanScope>();
            CreateWindowAndRefresh();

            var ours = new HashSet<string>
            {
                typeof(MigrationOkScope).FullName,
                typeof(MigrationOrphanScope).FullName,
                typeof(MigrationStaleScope).FullName,
            };

            var actual = new List<string>();
            foreach (var s in (IList)Field(_window, "_scopes"))
            {
                var n = (string)Field(s, "Name");
                if (ours.Contains(n)) actual.Add(n);
            }

            var expected = new List<string>(actual);
            expected.Sort(StringComparer.Ordinal);

            Assert.AreEqual(3, actual.Count);
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void Scope_BrokenConfigure_DoesNotHideOtherScopes()
        {
            var okName = typeof(MigrationOkScope).FullName;
            var brokenName = typeof(MigrationBrokenScope).FullName;
            Create<MigrationOkScope>();
            Create<MigrationBrokenScope>();
            CreateWindowAndRefresh();

            Assert.IsTrue(string.IsNullOrEmpty((string)Field(_window, "_errorMessage")));

            var broken = FindScope(brokenName);
            Assert.IsNotNull(broken);
            var error = (string)Field(broken, "Error");
            Assert.IsFalse(string.IsNullOrEmpty(error));
            StringAssert.Contains("broken scope", error);

            var ok = FindScope(okName);
            Assert.IsNotNull(ok);
            Assert.IsTrue(string.IsNullOrEmpty((string)Field(ok, "Error")));
            Assert.AreEqual(1, RowsOf(ok).Count);
            Assert.AreEqual("Ok", StatusOf(ok));
        }

        [Test]
        public void Scope_ExistingInstance_IsNotMarkedAuto()
        {
            var name = typeof(MigrationOkScope).FullName;
            Create<MigrationOkScope>();
            CreateWindowAndRefresh();

            var view = FindScope(name);
            Assert.IsNotNull(view);
            Assert.IsFalse((bool)Field(view, "IsAuto"));
        }

        [Test]
        public void AutoLoad_On_CreatesScopeForTypeWithoutInstance()
        {
            var name = typeof(MigrationMultiScope).FullName;
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<MigrationMultiScope>().Length);

            CreateWindowAndRefresh(includeAll: true);

            var view = FindScope(name);
            Assert.IsNotNull(view);
            Assert.IsTrue((bool)Field(view, "IsAuto"));
            Assert.AreEqual(2, RowsOf(view).Count);
        }

        [Test]
        public void AutoLoad_Off_DoesNotCreateScopeForTypeWithoutInstance()
        {
            Create<MigrationOkScope>();
            CreateWindowAndRefresh(includeAll: false);

            Assert.IsNull(FindScope(typeof(MigrationMultiScope).FullName));
            Assert.IsNull(FindScope(typeof(MigrationStaleScope).FullName));
        }

        [Test]
        public void AutoLoad_TemporaryInstances_AreDestroyedOnNextRefresh()
        {
            CreateWindowAndRefresh(includeAll: true);
            Assert.GreaterOrEqual(Resources.FindObjectsOfTypeAll<MigrationMultiScope>().Length, 1);

            SetWindowField("_includeAllTypes", false);
            Refresh();

            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<MigrationMultiScope>().Length);
        }

        [Test]
        public void Refresh_Twice_DoesNotDuplicateEntries()
        {
            var name = typeof(MigrationMultiScope).FullName;
            Create<MigrationMultiScope>();
            CreateWindowAndRefresh();

            int scopes1 = ((IList)Field(_window, "_scopes")).Count;
            int entries1 = CountEntries(name);

            Refresh();

            Assert.AreEqual(scopes1, ((IList)Field(_window, "_scopes")).Count);
            Assert.AreEqual(entries1, CountEntries(name));
            Assert.AreEqual(2, entries1);
        }

        [Test]
        public void Renames_RegisteredOnTable_AreListed()
        {
            var name = typeof(MigrationOkScope).FullName;
            var scope = Create<MigrationOkScope>();
            var table = scope.GetSaveSystem().Definition.Migrations;
            Assert.IsNotNull(table);
            table.RegisterRenamed(0x11UL, 0x22UL, "Old.Health", "Game.Domain.Health");

            CreateWindowAndRefresh();

            bool found = false;
            foreach (var item in (IList)Field(_window, "_renames"))
            {
                var t = item.GetType();
                if ((string)t.GetField("Item1").GetValue(item) != name) continue;
                var entry = (RenamedEntry)t.GetField("Item2").GetValue(item);
                Assert.AreEqual("Old.Health", entry.OldFullName);
                Assert.AreEqual("Game.Domain.Health", entry.NewFullName);
                found = true;
            }
            Assert.IsTrue(found);

            var view = FindScope(name);
            Assert.AreEqual(1, ((IList)Field(view, "Renames")).Count);
        }

        [Test]
        public void RowMatches_TypeNameSubstring_CaseInsensitive()
        {
            Assert.IsTrue(RowMatches("Game.Domain.Health", 1, 2, 3, "health"));
            Assert.IsTrue(RowMatches("Game.Domain.Health", 1, 2, 3, "HEALTH"));
            Assert.IsTrue(RowMatches("Game.Domain.Health", 1, 2, 3, "domain.he"));
        }

        [Test]
        public void RowMatches_NoMatch_ReturnsFalse()
        {
            Assert.IsFalse(RowMatches("Game.Domain.Health", 1, 2, 3, "zzz"));
        }

        [Test]
        public void RowMatches_HashWithPrefix_MatchesAnyOfThree()
        {
            const ulong a = 0xAAAA000000000001UL;
            const ulong b = 0xBBBB000000000002UL;
            const ulong c = 0xCCCC000000000003UL;

            Assert.IsTrue(RowMatches("X", a, b, c, "0x" + a.ToString("X16")));
            Assert.IsTrue(RowMatches("X", a, b, c, "0x" + b.ToString("X16")));
            Assert.IsTrue(RowMatches("X", a, b, c, "0x" + c.ToString("X16")));
        }

        [Test]
        public void RowMatches_HashWithoutPrefix_AndLowerCase_Matches()
        {
            const ulong a = 0xABCDEF0000000001UL;

            Assert.IsTrue(RowMatches("X", a, 2, 3, a.ToString("X16")));
            Assert.IsTrue(RowMatches("X", a, 2, 3, a.ToString("x16")));
            Assert.IsTrue(RowMatches("X", a, 2, 3, "abcdef"));
        }

        [Test]
        public void RowMatches_PrefixOnly_MatchesEverything()
        {
            Assert.IsTrue(RowMatches("X", 1, 2, 3, "0x"));
        }

        [Test]
        public void Hex_FormatsAsPrefixedSixteenDigits()
        {
            var m = _windowType.GetMethod("Hex", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Hex not found");

            Assert.AreEqual("0x0000000000000001", (string)m.Invoke(null, new object[] { 1UL }));
            Assert.AreEqual("0xFFFFFFFFFFFFFFFF", (string)m.Invoke(null, new object[] { ulong.MaxValue }));
        }
    }
}
#endif
