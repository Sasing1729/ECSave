using ECSave;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.MinimalE2E
{
    public sealed class MigrationTestScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder builder)
        {
            var impl = (SaveDefinitionBuilderImpl)builder;
            impl.RegisterMigrationStep(new HealthV1ToV2Migration());
            builder.GameDataVersion(1);
            builder.WorldId("MigrationTestScopeWorld");
        }
    }

#if UNITY_EDITOR
    public static class MigrationTestScopeMenu
    {
        [MenuItem("Window/ECSave/Debug/Create Migration Test Scope")]
        public static void Create()
        {
            if (Resources.FindObjectsOfTypeAll<MigrationTestScope>().Length > 0)
            {
                Debug.Log("[ECSave] MigrationTestScope already exists.");
                return;
            }

            var scope = ScriptableObject.CreateInstance<MigrationTestScope>();
            scope.hideFlags = HideFlags.HideAndDontSave;
            Debug.Log("[ECSave] MigrationTestScope created.");
        }

        [MenuItem("Window/ECSave/Debug/Destroy Migration Test Scope")]
        public static void Destroy()
        {
            var all = Resources.FindObjectsOfTypeAll<MigrationTestScope>();
            for (int i = 0; i < all.Length; i++)
                Object.DestroyImmediate(all[i]);
            Debug.Log("[ECSave] MigrationTestScope destroyed: " + all.Length);
        }
    }
#endif
}
