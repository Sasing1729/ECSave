using System;
using System.Collections.Generic;
using ECSave;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public sealed class GenTombstoneScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterIdentity<GenPlayerId>();
            b.WorldId("GenTombstoneWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.RegisterDestroyedEntities();
        }
    }

    public sealed class GenTombstoneNoRegisterScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenTombstoneWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.RegisterDestroyedEntities();
        }
    }

    public sealed class GenTombstoneDisabledScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.RegisterIdentity<GenPlayerId>();
            b.WorldId("GenTombstoneWorld");
        }

        // ConfigureSubScene 未オーバーライド → RegisterDestroyedEntities 無効
    }
}
