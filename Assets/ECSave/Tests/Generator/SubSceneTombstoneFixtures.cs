using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// 意図的に <c>RegisterDestroyedEntities()</c> を呼びつつ
    /// <c>RegisterIdentity&lt;T&gt;()</c> を欠いた状態を作るフィクスチャ。
    ///
    /// <para>
    /// ECSAVER021 は通常「Tombstone が機能しない」という警告だが、本フィクスチャは
    /// <c>SubSceneTombstoneEndToEndTests.MarkDestroyed_NoRegisterIdentity_NoEffect</c>
    /// の検証シナリオ（識別型未登録時は Tombstone が機能せず既存 Entity が残る）
    /// を成立させるために、意図的に警告を出す構成を取る。
    /// </para>
    ///
    /// <para>
    /// 実運用コードで同様の構成を書いた場合は ECSAVER021 で警告されるべきなので、
    /// プロジェクト全体（.editorconfig や ruleset）での抑制は行わず、
    /// このクラスだけ <see cref="SuppressMessageAttribute"/> で局所抑制する。
    /// </para>
    /// </summary>
    [SuppressMessage("ECSave", "ECSAVER021",
        Justification = "Test fixture intentionally omits RegisterIdentity<T> to verify " +
                        "that tombstones are ignored when no identity type is registered. " +
                        "See SubSceneTombstoneEndToEndTests.MarkDestroyed_NoRegisterIdentity_NoEffect.")]
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
