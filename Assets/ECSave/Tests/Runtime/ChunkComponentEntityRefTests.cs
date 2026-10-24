using System;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// B-5: ChunkComponent 内の Entity 参照のラウンドトリップと検証。
    /// Chunk も Component/Buffer/Shared と同じ EntityRef パイプラインを通ることを検証する。
    /// </summary>
    public unsafe class ChunkComponentEntityRefTests
    {
        private const string WorldName = "ChunkRefWorld";
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World(WorldName);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private SaveSystem CreateSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule(), new ChunkWithRefModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, WorldName);
            return new SaveSystem(def);
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        private static EntityQuery ChunkRefQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<ChunkWithRef>() },
            });

        private static ChunkWithRef GetChunkValue(EntityManager em)
        {
            var q = ChunkRefQuery(em);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(1, chunks.Length,
                "Expected exactly one chunk with ChunkComponent<ChunkWithRef>");
            var entities = chunks[0].GetNativeArray(em.GetEntityTypeHandle());
            Assert.Greater(entities.Length, 0);
            return em.GetChunkComponentData<ChunkWithRef>(entities[0]);
        }

        private static int FindChunkSectionOffset(byte[] data)
        {
            fixed (byte* p = data)
            {
                var view = new ByteView(p, data.Length);
                var reader = new FormatReader(view);
                reader.ReadHeader();
                var table = reader.ReadSectionTable();
                for (int i = 0; i < table.Count; i++)
                {
                    if (table.Entries[i].Kind == SectionKind.ChunkComponent)
                        return (int)table.Entries[i].Offset;
                }
            }
            return -1;
        }

        private static int ReadTypeDescriptorSize(byte[] data, int sectionOffset)
        {
            fixed (byte* p = data)
                return (int)(*(uint*)(p + sectionOffset));
        }

        /// <summary>
        /// 最初の ChunkComponent セクションの records 領域先頭の絶対オフセットと
        /// dataStride を返す。Body レイアウト:
        ///   [GroupCount u32][Flags u32][Stride u32][MemberOffset (G+1)*4][MemberEntityIndex memberCount*4][records]
        /// </summary>
        private static int FindRecordsOffset(byte[] data, out int stride)
        {
            int sectionOffset = FindChunkSectionOffset(data);
            Assert.GreaterOrEqual(sectionOffset, 0, "chunk section not found");

            int tdSize = ReadTypeDescriptorSize(data, sectionOffset);
            int bodyOffset = sectionOffset + tdSize;

            fixed (byte* p = data)
            {
                byte* body = p + bodyOffset;
                uint groupCount = *(uint*)body;
                stride = (int)*(uint*)(body + 8);
                Assert.Greater(stride, 0);

                byte* mo = body + 12;
                uint* moPtr = (uint*)mo;
                uint memberCount = moPtr[groupCount];

                byte* mei = mo + (groupCount + 1) * 4;
                byte* records = mei + (int)memberCount * 4;

                return (int)(records - p);
            }
        }

        private static void RecomputeChecksum(byte[] data)
        {
            fixed (byte* p = data)
            {
                int checksumEnd = data.Length - ECSaveFormatConstants.FooterSize;
                ulong ck = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));
                *(ulong*)(p + checksumEnd) = ck;
            }
        }

        [Test]
        public void RoundTrip_WithEntityReference_Resolves()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var target = em.CreateEntity();
            em.AddComponentData(target, new Health { Current = 50, Max = 100 });
            em.AddComponentData(target, new PlayerId { Value = 1 });

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddComponentData(holder, new PlayerId { Value = 2 });
            em.AddChunkComponentData<ChunkWithRef>(holder);

            var chunk = em.GetChunk(holder);
            em.SetChunkComponentData(chunk, new ChunkWithRef
            {
                Target = target,
                Value = 42,
            });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

            var em2 = _world.EntityManager;
            var chunkValue = GetChunkValue(em2);

            Assert.AreNotEqual(Entity.Null, chunkValue.Target);
            Assert.IsTrue(em2.Exists(chunkValue.Target));
            Assert.AreEqual(42, chunkValue.Value);

            // Target は PlayerId=1 を持つ entity に解決されているはず
            Assert.IsTrue(em2.HasComponent<PlayerId>(chunkValue.Target));
            Assert.AreEqual(1, em2.GetComponentData<PlayerId>(chunkValue.Target).Value);
            Assert.AreEqual(50, em2.GetComponentData<Health>(chunkValue.Target).Current);
        }

        [Test]
        public void RoundTrip_WithNullReference_Preserved()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddChunkComponentData<ChunkWithRef>(holder);

            var chunk = em.GetChunk(holder);
            em.SetChunkComponentData(chunk, new ChunkWithRef
            {
                Target = Entity.Null,
                Value = 99,
            });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var chunkValue = GetChunkValue(_world.EntityManager);
            Assert.AreEqual(Entity.Null, chunkValue.Target);
            Assert.AreEqual(99, chunkValue.Value);
        }

        [Test]
        public void TamperedPid_OutOfRange_ReturnsEntityReferenceInvalid()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var target = em.CreateEntity();
            em.AddComponentData(target, new Health { Current = 50, Max = 100 });

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddChunkComponentData<ChunkWithRef>(holder);

            var chunk = em.GetChunk(holder);
            em.SetChunkComponentData(chunk, new ChunkWithRef
            {
                Target = target,
                Value = 42,
            });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);

            int recordsOffset = FindRecordsOffset(raw, out int stride);
            Assert.Greater(stride, 0);

            // 最初のレコードの pid（dataStride レイアウト上の offset 0）を改ざん。
            // refs=[0] のため、Entity.Target の pid はレコード先頭 4B に位置する。
            fixed (byte* p = raw)
            {
                *(uint*)(p + recordsOffset) = 0x00000100u;  // pid=256 (out of range)
            }
            RecomputeChecksum(raw);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, raw,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.EntityReferenceInvalid, load.ErrorCode);
        }

        [Test]
        public void UnresolvedReference_TargetNotSaved_EmitsWarning()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            // Bare entity: 登録済みコンポーネントを持たない → _idOf に入らない
            var bare = em.CreateEntity();

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddChunkComponentData<ChunkWithRef>(holder);

            var chunk = em.GetChunk(holder);
            em.SetChunkComponentData(chunk, new ChunkWithRef
            {
                Target = bare,
                Value = 77,
            });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

            bool hasUnresolvedWarning = false;
            for (int i = 0; i < load.Warnings.Count; i++)
                if (load.Warnings[i].Kind == LoadWarningKind.EntityReferenceUnresolved)
                    hasUnresolvedWarning = true;
            Assert.IsTrue(hasUnresolvedWarning,
                "UnresolvedRef should emit EntityReferenceUnresolved warning");

            var chunkValue = GetChunkValue(_world.EntityManager);
            Assert.AreEqual(Entity.Null, chunkValue.Target,
                "UnresolvedRef should be expanded to Entity.Null");
            Assert.AreEqual(77, chunkValue.Value);
        }
    }
}
