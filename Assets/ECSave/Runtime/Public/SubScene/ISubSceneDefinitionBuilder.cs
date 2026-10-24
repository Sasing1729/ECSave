using System;
using System.Collections.Generic;
using Unity.Entities;

namespace ECSave
{
    public interface ISubSceneDefinitionBuilder
    {
        ISubSceneDefinitionBuilder RegisterSceneLoadState();
        ISubSceneDefinitionBuilder RegisterDestroyedEntities();
        ISubSceneDefinitionBuilder WithTombstoneCapacity(int capacity);
        ISubSceneDefinitionBuilder WithContentVerification(ContentVerificationMode mode);
        ISubSceneDefinitionBuilder WithStreamingConsistency(StreamingConsistencyMode mode);
        ISubSceneDefinitionBuilder WithSceneStateRollback();
        ISubSceneDefinitionBuilder RegisterPrefabMigration<TPrefabMigration>() where TPrefabMigration : class, IPrefabMigration;

        ISubSceneDefinitionBuilder WithSceneStateProvider(Func<IReadOnlyList<SceneLoadInfo>> provider);
        ISubSceneDefinitionBuilder WithSceneRestoreCallback(Func<SceneLoadRequest[], SceneLoadOutcome[]> restore);
        ISubSceneDefinitionBuilder WithPrefabIdentityProvider(Func<Entity, PrefabIdentityInfo?> provider);
    }
}
