using ECSave;
using ECSave.Internal;
using NUnit.Framework;
using System;
using Unity.Entities.Content;
using UnityEngine;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// <see cref="AssetReferenceRegistry"/> の境界ケース。
    ///
    /// <para>
    /// 既存の <c>AssetReferenceRegistryTests</c> では「ゼロID・往復・未登録・冪等・
    /// クリア・型独立性」のみを検証している。
    /// 本テストでは <see cref="AssetRefConstants"/> の特殊値の扱いを検証する。
    /// </para>
    ///
    /// <para>
    /// <b>既存 <c>DummyAsset</c> を再利用</b>: <c>AssetReferenceRegistryTests.cs</c> 内の
    /// <c>internal sealed class DummyAsset</c> を同一 namespace から参照する。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class AssetReferenceRegistryAdditionalTests
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

        // ═══════════════════════════════════════════
        // TryGetId の戻り値
        // ═══════════════════════════════════════════

        [Test]
        public void TryGetId_NotRegistered_ReturnsFalseAndNullRef()
        {
            var reference = default(WeakObjectReference<DummyAsset>);

            Assert.IsFalse(AssetReferenceRegistry.TryGetId(reference, out var id));
            Assert.AreEqual(AssetRefConstants.NullRef, id,
                "TryGetId on unregistered reference must return NullRef (0) as stableId");
        }

        // ═══════════════════════════════════════════
        // TryResolve の特殊値
        // ═══════════════════════════════════════════

        [Test]
        public void TryResolve_NullRef_ReturnsFalse()
        {
            // NullRef (0) は「無効」を意味し、レジストリには登録されていない
            Assert.IsFalse(
                AssetReferenceRegistry.TryResolve<DummyAsset>(AssetRefConstants.NullRef, out _));
        }

        [Test]
        public void TryResolve_UnresolvedRef_ReturnsFalse()
        {
            // UnresolvedRef (ulong.MaxValue) は「保存時に解決不能」マーカー。
            // レジストリは解釈せず、通常の未登録 ID として扱う。
            Assert.IsFalse(
                AssetReferenceRegistry.TryResolve<DummyAsset>(AssetRefConstants.UnresolvedRef, out _));
        }

        // ═══════════════════════════════════════════
        // クリア後の再登録
        // ═══════════════════════════════════════════

        [Test]
        public void Clear_Then_Register_Again_Works()
        {
            var reference = default(WeakObjectReference<DummyAsset>);

            AssetReferenceRegistry.Register(42UL, reference);
            Assert.IsTrue(AssetReferenceRegistry.TryResolve<DummyAsset>(42UL, out _));

            AssetReferenceRegistry.Clear();

            Assert.IsFalse(AssetReferenceRegistry.TryResolve<DummyAsset>(42UL, out _));

            // 再登録可能
            Assert.DoesNotThrow(() => AssetReferenceRegistry.Register(42UL, reference));
            Assert.IsTrue(AssetReferenceRegistry.TryResolve<DummyAsset>(42UL, out _));
        }

        // ═══════════════════════════════════════════
        // 登録後の TryGetId 往復
        // ═══════════════════════════════════════════

        [Test]
        public void TryGetId_AfterRegister_ReturnsNonZeroId()
        {
            var reference = default(WeakObjectReference<DummyAsset>);
            const ulong ExpectedId = 12345UL;

            AssetReferenceRegistry.Register(ExpectedId, reference);

            Assert.IsTrue(AssetReferenceRegistry.TryGetId(reference, out var id));
            Assert.AreNotEqual(AssetRefConstants.NullRef, id);
            Assert.AreEqual(ExpectedId, id);
        }
    }
}
