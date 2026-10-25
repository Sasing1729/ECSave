using ECSave;
using ECSave.Internal;
using NUnit.Framework;
using System;
using Unity.Entities.Content;
using UnityEngine;

namespace ECSave.Tests.Runtime
{
    // テスト用のダミーアセット型。
    //
    // WeakObjectReference<T> は T : UnityEngine.Object の制約を持つため、
    // DummyAsset も UnityEngine.Object を継承する必要がある。
    // テストでは default(WeakObjectReference<T>) のみを使うため、
    // インスタンス生成は不要（UnityEngine.Object のコンストラクタ制約を回避）。
    internal sealed class DummyAsset : UnityEngine.Object { }

    internal sealed class DummyAsset2 : UnityEngine.Object { }

    /// <summary>
    /// <see cref="AssetReferenceRegistry"/> の基本動作検証。
    ///
    /// <para>
    /// <b>注意</b>: 実 Unity の <c>WeakObjectReference&lt;T&gt;</c> は内部 ID を
    /// 持つため、任意の ID で複数インスタンスを作ることは難しい。
    /// 本テストは <c>default(WeakObjectReference&lt;T&gt;)</c> を使った
    /// 「同一参照の往復」と「例外・冪等性」を検証する。
    /// 異なる参照間の衝突検出は <c>IAssetReferenceCodec</c> を介した
    /// ユーザー側の責務になる。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class AssetReferenceRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            AssetReferenceRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AssetReferenceRegistry.Clear();
        }

        [Test]
        public void Register_ZeroStableId_Throws()
        {
            var reference = default(WeakObjectReference<DummyAsset>);

            var ex = Assert.Throws<ArgumentException>(() =>
                AssetReferenceRegistry.Register(AssetRefConstants.NullRef, reference));

            Assert.AreEqual("stableId", ex.ParamName);
        }

        [Test]
        public void Register_AndResolve_RoundTrip()
        {
            var reference = default(WeakObjectReference<DummyAsset>);

            AssetReferenceRegistry.Register(42UL, reference);

            Assert.IsTrue(AssetReferenceRegistry.TryResolve<DummyAsset>(42UL, out var resolved));
            Assert.AreEqual(reference, resolved);
        }

        [Test]
        public void TryResolve_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(AssetReferenceRegistry.TryResolve<DummyAsset>(999UL, out _));
        }

        [Test]
        public void TryGetId_AfterRegister_ReturnsId()
        {
            var reference = default(WeakObjectReference<DummyAsset>);
            AssetReferenceRegistry.Register(42UL, reference);

            Assert.IsTrue(AssetReferenceRegistry.TryGetId(reference, out var id));
            Assert.AreEqual(42UL, id);
        }

        [Test]
        public void TryGetId_NotRegistered_ReturnsFalse()
        {
            var reference = default(WeakObjectReference<DummyAsset>);

            Assert.IsFalse(AssetReferenceRegistry.TryGetId(reference, out var id));
            Assert.AreEqual(0UL, id);
        }

        [Test]
        public void Register_SameIdSameReference_IsIdempotent()
        {
            var reference = default(WeakObjectReference<DummyAsset>);

            Assert.DoesNotThrow(() => AssetReferenceRegistry.Register(42UL, reference));
            Assert.DoesNotThrow(() => AssetReferenceRegistry.Register(42UL, reference));

            Assert.IsTrue(AssetReferenceRegistry.TryResolve<DummyAsset>(42UL, out _));
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var reference = default(WeakObjectReference<DummyAsset>);
            AssetReferenceRegistry.Register(42UL, reference);

            AssetReferenceRegistry.Clear();

            Assert.IsFalse(AssetReferenceRegistry.TryResolve<DummyAsset>(42UL, out _));
        }

        [Test]
        public void DifferentTypes_AreIndependent()
        {
            // DummyAsset と DummyAsset2 は別の型として独立して扱われる。
            // 両方とも UnityEngine.Object 派生なので WeakObjectReference<T> の制約を満たす。
            var dummy1 = default(WeakObjectReference<DummyAsset>);
            var dummy2 = default(WeakObjectReference<DummyAsset2>);

            AssetReferenceRegistry.Register(42UL, dummy1);
            AssetReferenceRegistry.Register(42UL, dummy2);

            Assert.IsTrue(AssetReferenceRegistry.TryResolve<DummyAsset>(42UL, out var r1));
            Assert.IsTrue(AssetReferenceRegistry.TryResolve<DummyAsset2>(42UL, out var r2));

            Assert.AreEqual(dummy1, r1);
            Assert.AreEqual(dummy2, r2);
        }
    }

    // ═══════════════════════════════════════════
    // 案 B: IAssetReferenceCodec<T> のモック実装テスト
    // ═══════════════════════════════════════════

    /// <summary>
    /// テスト用のマスターデータ（ID → 参照の逆引きを模倣）。
    ///
    /// <para>
    /// 実 Unity の <c>WeakObjectReference&lt;T&gt;</c> はアセットからしか
    /// 構築できないため、本テストでは <c>default(WeakObjectReference&lt;T&gt;)</c>
    /// のみを使い、「型としての契約」と「ロジックとしての正しさ」を検証する。
    /// </para>
    /// </summary>
    internal sealed class MockAssetCodec : IAssetReferenceCodec<DummyAsset>
    {
        private readonly System.Collections.Generic.Dictionary<ulong, WeakObjectReference<DummyAsset>> _byId
            = new System.Collections.Generic.Dictionary<ulong, WeakObjectReference<DummyAsset>>();
        private readonly System.Collections.Generic.Dictionary<WeakObjectReference<DummyAsset>, ulong> _byRef
            = new System.Collections.Generic.Dictionary<WeakObjectReference<DummyAsset>, ulong>();

        public void Add(ulong id, WeakObjectReference<DummyAsset> reference)
        {
            _byId[id] = reference;
            _byRef[reference] = id;
        }

        public ulong GetStableId(WeakObjectReference<DummyAsset> reference)
        {
            return _byRef.TryGetValue(reference, out var id) ? id : 0UL;
        }

        public bool TryResolve(ulong stableId, out WeakObjectReference<DummyAsset> reference)
        {
            return _byId.TryGetValue(stableId, out reference);
        }
    }

    [TestFixture]
    public sealed class AssetReferenceCodecTests
    {
        [Test]
        public void Codec_RoundTrip()
        {
            var codec = new MockAssetCodec();
            var reference = default(WeakObjectReference<DummyAsset>);

            codec.Add(100UL, reference);

            Assert.AreEqual(100UL, codec.GetStableId(reference));

            Assert.IsTrue(codec.TryResolve(100UL, out var resolved));
            Assert.AreEqual(reference, resolved);
        }

        [Test]
        public void Codec_UnknownId_ReturnsZeroAndFalse()
        {
            var codec = new MockAssetCodec();

            Assert.AreEqual(0UL, codec.GetStableId(default));
            Assert.IsFalse(codec.TryResolve(999UL, out _));
        }

        [Test]
        public void Codec_AsInterface_WorksPolymorphically()
        {
            IAssetReferenceCodec<DummyAsset> codec = new MockAssetCodec();
            ((MockAssetCodec)codec).Add(7UL, default);

            Assert.AreEqual(7UL, codec.GetStableId(default));
            Assert.IsTrue(codec.TryResolve(7UL, out _));
        }
    }
}
