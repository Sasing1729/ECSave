# ECSave

Unity DOTS（Entities）向けの **セーブ / ロードシステム**。
スキーマ進化（Migration）、SubScene 状態管理、Prefab マイグレーション、アセット参照の永続化までカバーします。

> **Status**: pre-release。API は予告なく変更される場合があります。

---

## 目次

- [特徴](#特徴)
- [動作環境](#動作環境)
- [インストール](#インストール)
- [クイックスタート](#クイックスタート)
- [基本](#基本)
  - [コンポーネントを登録する](#コンポーネントを登録する)
  - [SaveScope を作る](#savescope-を作る)
  - [セーブする](#セーブする)
  - [ロードする](#ロードする)
  - [結果を検査する](#結果を検査する)
- [応用](#応用)
  - [スキーマ進化（IComponentMigration）](#スキーマ進化icomponentmigration)
  - [Entity を含む型のスキーマ進化（IPidMigration）](#entity-を含む型のスキーマ進化ipidmigration)
  - [Identity（識別子）](#identity識別子)
  - [削除ポリシー](#削除ポリシー)
  - [復元スコープ](#復元スコープ)
  - [SubScene](#subscene)
  - [Prefab マイグレーション](#prefab-マイグレーション)
  - [アセット参照（WeakObjectReference）](#アセット参照weakobjectreference)
  - [複数 Scope で binder を共有する](#複数-scope-で-binder-を共有する)
- [高度な使い方](#高度な使い方)
  - [NonBlocking API](#nonblocking-api)
  - [カスタム Module](#カスタム-module)
  - [診断ログ](#診断ログ)
- [リファレンス](#リファレンス)
  - [SaveLoadErrorCode](#saveloaderrorcode)
  - [LoadWarningKind](#loadwarningkind)
  - [診断コード（Source Generator）](#診断コードsource-generator)
  - [定数](#定数)
- [設計思想](#設計思想)
- [既知の制約](#既知の制約)
- [FAQ](#faq)

---

## 特徴

| 機能 | 説明 |
|---|---|
| **型安全な自動生成** | Source Generator が各コンポーネント用のモジュールクラスを生成 |
| **スキーマ進化** | `IComponentMigration<TOld, TNew>` による旧形式からの移行 |
| **Entity 参照の永続化** | `Entity` フィールドを pid（uint）として保存、ロード時に復元 |
| **PidMigration** | **Entity を含む型**でもスキーマ進化可能 |
| **SubScene 状態管理** | シーンの Load/Unload 状態、Tombstone、Prefab マイグレーション |
| **アセット参照** | `WeakObjectReference<T>` を安全に保存する手段を提供 |
| **LZ4 圧縮** | 大きいセーブデータを自動圧縮（爆弾対策付き） |
| **診断** | 型の制約、レイアウト整合性、危険パターンをコンパイル時に警告 |

---

## 動作環境

| 項目 | 要件 |
|---|---|
| Unity | 2022.3 LTS 以上（Entities 1.0+） |
| Burst | 1.8+ |
| Collections | 2.1+ |
| C# | 9.0 以上 |
| 言語 | C#（IL2CPP 対応） |

---

## インストール

1. `ECSave.unitypackage` を Unity プロジェクトにインポート
2. `Assets/ECSave/` に Runtime / SourceGenerator が展開される
3. Source Generator は自動的に有効化（Unity の `RoslynAnalyzer` 設定不要）
4. 確認: 空の `SaveScope` を定義すると `ECSave.Generated` 名前空間にクラスが生成される

```
Packages/
  ECSave/
    Runtime/
    SourceGenerator/
    Tests/
```

---

## クイックスタート

最小構成の例です。

```csharp
using ECSave;
using Unity.Entities;

// 1. 保存対象のコンポーネント
public struct Health : IComponentData
{
    public int Current;
    public int Max;
}

// 2. SaveScope（登録設定）
public sealed class GameScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
        b.GameDataVersion(1);
    }
}

// 3. 保存
public async UniTask SaveGame(World world, GameScope scope)
{
    var sys = scope.GetSaveSystem();
    var result = await sys.Serialize(world);

    if (result.IsSuccess)
        System.IO.File.WriteAllBytes("save.bin", result.Data);
    else
        UnityEngine.Debug.LogError(result.ErrorCode.GetUserFacingMessage());
}

// 4. ロード
public async UniTask LoadGame(World world, GameScope scope)
{
    var sys = scope.GetSaveSystem();
    var data = System.IO.File.ReadAllBytes("save.bin");
    var result = await sys.Deserialize(
        world, data,
        RestorationScope.EntireWorld,
        DeletionPolicy.ByRegistration);

    if (!result.IsSuccess)
        UnityEngine.Debug.LogError(result.ErrorCode.GetUserFacingMessage());
}
```

---

## 基本

### コンポーネントを登録する

`SaveScope.Configure` で保存対象を登録します。登録できる種類は 4 つ：

```csharp
protected override void Configure(ISaveDefinitionBuilder b)
{
    // 通常コンポーネント
    b.RegisterComponent<Health>();
    b.RegisterComponent<Position>();

    // DynamicBuffer
    b.RegisterBuffer<InventoryItem>();

    // ISharedComponentData
    b.RegisterSharedComponent<Faction>();

    // ChunkComponent
    b.RegisterChunkComponent<ChunkMetadata>();

    // フィルタ付き（Query を絞る）
    b.RegisterComponent<Enemy>().WithAll<Hostile>();

    // 旧名からのリネーム
    b.RegisterComponent<Health>().RenamedFrom("Game.Domain.OldHealth");

    // メタ情報
    b.GameDataVersion(3);
    b.WorldId("MyGameWorld");
}
```

### SaveScope を作る

`SaveScope` は `ScriptableObject` です。Asset として作成するか、`ScriptableObject.CreateInstance` で生成します。

```csharp
// Editor で作成する場合
[CreateAssetMenu(menuName = "ECSave/GameScope")]
public sealed class GameScopeAsset : GameScope { }
```

### セーブする

```csharp
var sys = scope.GetSaveSystem();
var result = await sys.Serialize(world);

if (result.IsSuccess)
{
    byte[] data = result.Data;
    // ファイル保存、ネットワーク送信など
}
```

- **同期的実行**: 既定では `Serialize` はメインスレッド上で同期的に実行され、完了済み `UniTask` を返します。
- **非同期実行**: `SerializeAsyncNonBlocking` または `StreamingConsistencyMode.FrameBoundary` を使うとフレームを跨いで実行されます。

### ロードする

```csharp
var result = await sys.Deserialize(
    world,
    data,
    RestorationScope.EntireWorld,
    DeletionPolicy.ByRegistration);

if (result.IsSuccess)
{
    int entityCount = result.EntityCount;
    var warnings = result.Warnings;  // 警告がある場合
}
```

### 結果を検査する

```csharp
if (!result.IsSuccess)
{
    // 人間可読な説明 + 対処法
    Debug.LogError(result.ErrorCode.GetUserFacingMessage());
    // 例: "The save data checksum does not match. Restore from a backup save."
}
```

`SaveLoadErrorCodeExtensions` が以下を提供：

| メソッド | 用途 |
|---|---|
| `GetDescription()` | 何が起きたか |
| `GetRemediation()` | どうすればよいか |
| `GetUserFacingMessage()` | 上記を 1 行に |
| `GetCodeName()` | enum 名（ローカライズキー用途） |

---

## 応用

### スキーマ進化（IComponentMigration）

型のレイアウトが変わった場合、旧形式から新形式への変換を書けます。

```csharp
// 旧形式
public struct HealthV1 : IComponentData
{
    public int Value;
}

// 新形式
public struct HealthV2 : IComponentData
{
    public int Current;
    public int Max;
}

// Migration
public sealed class HealthV1ToV2 : IComponentMigration<HealthV1, HealthV2>
{
    public HealthV2 Migrate(in HealthV1 old)
        => new HealthV2 { Current = old.Value, Max = 100 };
}

// 登録
protected override void Configure(ISaveDefinitionBuilder b)
{
    b.RegisterComponent<HealthV2>();
    b.RegisterMigration<HealthV1ToV2>();

    // 型名を変えた場合
    b.RegisterComponent<HealthV2>().RenamedFrom("Game.Domain.HealthV1");
}
```

**制約**: `IComponentMigration` は `Entity` / `FixedString` / `FixedList` / ジェネリック型を **含めません**（`ECSAVE030`）。これらを含む場合は次節の `IPidMigration` を使います。

### Entity を含む型のスキーマ進化（IPidMigration）

`Entity` を含む型のスキーマを進化させたい場合、**PidForm**（`Entity` を `uint` に置換した型）を介します。

```csharp
// 旧形式（Entity 含む）
public struct AttackTargetV1 : IComponentData
{
    public Entity Owner;
    public int Value;
}

// 新形式（Entity 含む）
public struct AttackTargetV2 : IComponentData
{
    public Entity Owner;
    public int Value;
    public float Multiplier;
}

// PidForm（Entity → uint に置換）
public struct AttackTargetV1Pid { public uint Owner; public int Value; }
public struct AttackTargetV2Pid { public uint Owner; public int Value; public float Multiplier; }

// Migration
public sealed class AttackTargetV1ToV2 : IPidMigration<AttackTargetV1Pid, AttackTargetV2Pid>
{
    public AttackTargetV2Pid Migrate(in AttackTargetV1Pid old)
        => new AttackTargetV2Pid
        {
            Owner = old.Owner,       // pid をそのまま引き継ぐ
            Value = old.Value,
            Multiplier = 1.0f,
        };
}

// 登録
protected override void Configure(ISaveDefinitionBuilder b)
{
    b.RegisterComponent<AttackTargetV1>();
    b.RegisterComponent<AttackTargetV2>();

    b.RegisterPidForm<AttackTargetV1, AttackTargetV1Pid>();
    b.RegisterPidForm<AttackTargetV2, AttackTargetV2Pid>();
    b.RegisterPidMigration<AttackTargetV1ToV2>();
}
```

**PidForm の要件**:

| 項目 | 要件 |
|---|---|
| サイズ | SaveType の `RecordStride`（data stride）と一致 |
| `Entity` フィールド | `uint` に置換 |
| `FixedString` / `FixedList` | **同じ型をそのまま使う** |
| フィールド名・順序 | SaveType と一致 |

要件違反はコンパイル時に `ECSAVE051` / `ECSAVE052` / `ECSAVE053` で報告されます。

### Identity（識別子）

セッションを跨いで同じ Entity を識別したい場合、`RegisterIdentity<T>` を使います。

```csharp
public struct PlayerId : IComponentData { public int Value; }

protected override void Configure(ISaveDefinitionBuilder b)
{
    b.RegisterIdentity<PlayerId>();
}
```

- ロード時、World 側の既存 Entity が同じキーを持つ場合、**新規生成せず既存 Entity に復元**
- キーの重複は `ValidationFailed` で検出
- `RestorationScope.Custom` と組み合わせると、スコープ内 Entity のみを対象にできる

### 削除ポリシー

```csharp
public enum DeletionPolicy
{
    None = 0,            // 何も削除しない
    ByRegistration = 1,  // 登録済みモジュールが対象とする Entity のうち、保存データにないものを削除
    ByScope = 2,         // 復元スコープ内の Entity のうち、保存データにないものを削除
}
```

**既定は `ByRegistration`**。`ByScope` + `EntireWorld` の組み合わせは **大量削除** になるため、`ByScopeDeletionWarning` が出ます。

### 復元スコープ

```csharp
// 全 World
RestorationScope.EntireWorld

// スコープを絞る
var query = em.CreateEntityQuery(typeof(MyTag));
RestorationScope.Custom(query)
```

`Custom` の場合、`query` は **同じ World の EntityManager から作成** する必要があります（他 World のクエリは `ArgumentException`）。

### SubScene

SubScene の Load/Unload 状態を保存できます。

```csharp
protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
{
    b.RegisterSceneLoadState()
     .WithSceneStateProvider(GetCurrentScenes)
     .WithSceneRestoreCallback(RestoreScenes)
     .WithContentVerification(ContentVerificationMode.GuidAndContentHash)
     .WithSceneStateRollback();
}

private IReadOnlyList<SceneLoadInfo> GetCurrentScenes() { /* ... */ }
private SceneLoadOutcome[] RestoreScenes(SceneLoadRequest[] requests) { /* ... */ }
```

#### ContentVerificationMode

| モード | 検証内容 |
|---|---|
| `None` | 何もしない |
| `GuidOnly` | GUID と State の一致のみ（`ContentHash` は検証しない） |
| `GuidAndContentHash` | 上記 + `ContentHash` の一致 |

不一致は `SceneLoadStateMismatch` / `SceneContentHashMismatch` 警告になります。

#### Tombstone（破棄された Entity の追跡）

```csharp
b.RegisterDestroyedEntities();

// Entity を破棄する前に
SubSceneTombstone.MarkDestroyed<PlayerId>(em, playerId);
em.DestroyEntity(entity);
```

ロード時、破棄された `PlayerId` を持つ Entity は自動的に破棄されます。

### Prefab マイグレーション

Prefab のベイク済みスキーマが変わった場合、旧データを新スキーマに移行できます。

```csharp
public sealed class EnemyV1ToV2 : IPrefabMigration
{
    public ulong OldPrefabSchemaHash => 0xAAAA;
    public ulong NewPrefabSchemaHash => 0xBBBB;

    public void Migrate(ref PrefabMigrationContext ctx)
    {
        // ctx.Entity を直接操作
        ctx.EntityManager.AddComponentData(ctx.Entity, new EnemyV2Data { ... });
    }
}

protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
{
    b.WithPrefabIdentityProvider(GetPrefabIdentity)
     .RegisterPrefabMigration<EnemyV1ToV2>();
}

private PrefabIdentityInfo? GetPrefabIdentity(Entity e) { /* ... */ }
```

### アセット参照（WeakObjectReference）

`WeakObjectReference<T>` を保存対象に含めると `ECSAVE065` 警告が出ます。**そのまま保存するとビルド間で参照が壊れる** ためです。

#### 推奨パターン: 安定 ID に変換する

```csharp
// コンポーネント側は ulong の ID を持つ
public struct ItemData : IComponentData
{
    public ulong MasterId;
    public int Count;
}
```

```csharp
// 起動時（マスターデータ読み込み後）
AssetReferenceRegistry.Register<ItemMaster>(1UL, swordAsset);
AssetReferenceRegistry.Register<ItemMaster>(2UL, shieldAsset);

// 保存時（MasterId を書き込む）
AssetReferenceRegistry.TryGetId(component.Master, out var id);
save.MasterId = id;

// ロード時
if (AssetReferenceRegistry.TryResolve<ItemMaster>(save.MasterId, out var reference))
{
    // reference を使って処理
}
```

#### カスタム変換ロジック

```csharp
public sealed class ItemMasterCodec : IAssetReferenceCodec<ItemMaster>
{
    public ulong GetStableId(WeakObjectReference<ItemMaster> reference)
        => MasterDatabase.GetId(reference);

    public bool TryResolve(ulong stableId, out WeakObjectReference<ItemMaster> reference)
        => MasterDatabase.TryGet(stableId, out reference);
}
```

### 複数 Scope で binder を共有する

複数の `SaveScope` で同じ登録内容を共有したい場合、`[SaveScopeBinder]` を使います。

```csharp
// 共有 binder
public sealed class SharedBinder : ISaveModuleBinder
{
    public void BindModules(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Health>();
        b.RegisterComponent<Position>();
        b.RegisterBuffer<InventoryItem>();
    }
}

// モード A
[SaveScopeBinder(typeof(SharedBinder))]
public sealed class ModeAScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}

// モード B（共有 + モード固有の追加）
[SaveScopeBinder(typeof(SharedBinder))]
public sealed class ModeBScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<BossProgress>();  // 追加登録
        b.GameDataVersion(2);
    }
}
```

binder は `ISubSceneBinder` も実装可能。両方実装すると SubScene 設定も共有されます。

---

## 高度な使い方

### NonBlocking API

呼び出し元スレッドをブロックせず、次の `PostLateUpdate` で実行します。

```csharp
// 呼び出し元をブロックしない
var save = await sys.SerializeAsyncNonBlocking(world);
var load = await sys.DeserializeAsyncNonBlocking(world, data, scope);

// 注意: メインスレッドで .GetAwaiter().GetResult() を呼ぶとデッドロック
```

### カスタム Module

`SaveTypeModule<T>` を継承して、独自のシリアライズロジックを書けます。

```csharp
public sealed class MyCustomModule : SaveTypeModulePlain<MyComponent>
{
    public override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
    {
        // value には MyComponent の生バイトが入っている
        var v = default(MyComponent);
        UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref v), value, UnsafeUtility.SizeOf<MyComponent>());

        // 独自処理（バリデーション等）
        v.Value = Mathf.Max(0, v.Value);

        if (em.HasComponent<MyComponent>(entity))
            em.SetComponentData(entity, v);
        else
            em.AddComponentData(entity, v);
    }
}
```

### 診断ログ

```csharp
// Phase ごとの所要時間を計測
SerializeDiag.Enabled = true;
DeserializeDiag.Enabled = true;

var save = await sys.Serialize(world);
var load = await sys.Deserialize(world, data, ...);

SerializeDiag.Dump("MyGame", iterations: 1);
DeserializeDiag.Dump("MyGame", iterations: 1);
```

---

## リファレンス

### SaveLoadErrorCode

| コード | 説明 |
|---|---|
| `None` | エラーなし |
| `CorruptedData` | 構造破損 |
| `ChecksumMismatch` | チェックサム不一致（改ざん・破損） |
| `VersionMismatch` | バージョン非互換 |
| `SchemaMismatch` | スキーマ不一致（Migration 不足） |
| `MigrationFailed` | Migration 実行失敗 |
| `ValidationFailed` | 内容検証失敗 |
| `EntityReferenceInvalid` | Entity 参照不正 |
| `WorldMismatch` | World 名不一致 |
| `ConcurrentOperation` | 同時操作 |
| `WorldModificationFailed` | World 変更失敗（ロールバック済み） |
| `Cancelled` | キャンセル |
| `SerializationFailed` | シリアライズ失敗 |
| `SceneLoadFailed` | SubScene 復元失敗 |
| `SceneContentMismatch` | SubScene ContentHash 不一致 |
| `PrefabMigrationFailed` | Prefab マイグレーション失敗 |

### LoadWarningKind

| 種類 | 説明 |
|---|---|
| `EntityReferenceUnresolved` | 解決不能な Entity 参照 |
| `UnknownSectionIgnored` | 未知のセクション（スキップ） |
| `ChunkComponentMerged` | Chunk のマージ |
| `SceneLoadStateMismatch` | シーン状態不一致 |
| `SceneRestoreFailed` | シーン復元失敗（Optional） |
| `SceneContentHashMismatch` | ContentHash 不一致 |
| `PrefabMigrated` | Prefab マイグレーション実行 |
| `SceneStateRolledBack` | シーン状態ロールバック |
| `FixedStringMayBeTruncated` | FixedString 切詰め疑い |
| `PrefabSchemaNotRegistered` | Prefab スキーマ未登録 |
| `ByScopeDeletionWarning` | 大量削除の警告 |
| `PrefabSchemaMigrationFailed` | Prefab マイグレーション失敗 |
| `ChunkComponentEntityMissing` | Chunk メンバー復元失敗（回帰検出用） |

### 診断コード（Source Generator）

#### Error

| ID | 説明 |
|---|---|
| `ECSAVE001-003` | Select field の検証 |
| `ECSAVE010-018` | Identity の検証 |
| `ECSAVE022-029` | Save 型レイアウトの検証 |
| `ECSAVE030-032` | Migration の検証 |
| `ECSAVE033-035` | PrefabMigration の検証 |
| `ECSAVE040` | SaveScope の検証 |
| `ECSAVE044` | 型短名の衝突 |
| `ECSAVE048` | `FixedList<Entity>` 非対応 |
| `ECSAVE049` | 複数 `Configure` |
| `ECSAVE051-053` | PidForm / PidMigration の検証 |
| `ECSAVE061-063` | SaveScopeBinder の検証 |

#### Warning

| ID | 説明 |
|---|---|
| `ECSAVE041` | 型引数が静的解決できない |
| `ECSAVE042` | FrameBoundary の UpdateAfter 不足 |
| `ECSAVE043` | TombstoneCapacity が定数でない |
| `ECSAVE045-046` | ネスト深さ超過 |
| `ECSAVE047` | フィルタ非対応の種類 |
| `ECSAVE050` | RenamedFrom が定数でない |
| `ECSAVE065` | `WeakObjectReference<T>` の保存危険性 |
| `ECSAVER019` | SG 解析失敗 |
| `ECSAVER020` | SubScene 設定の重複 |
| `ECSAVER021` | Tombstone に Identity がない |

### 定数

```csharp
// Format 層
ECSaveFormatConstants.HeaderFixedSize      // 48
ECSaveFormatConstants.SectionEntrySize     // 48
ECSaveFormatConstants.MaxWorldIdLength     // 1024
ECSaveFormatConstants.MaxLogicalEntityCount // 10,000,000

// Entity 参照
EntityRefConstants.NullRef                 // 0xFFFFFFFF
EntityRefConstants.UnresolvedRef           // 0xFFFFFFFE

// Pid（PidMigration 用）
PidConstants.NullRef                       // 0xFFFFFFFF
PidConstants.UnresolvedRef                 // 0xFFFFFFFE

// アセット参照
AssetRefConstants.NullRef                  // 0
AssetRefConstants.UnresolvedRef            // ulong.MaxValue
```

---

## 設計思想

### 1. 型安全の重視

- Source Generator が各型のスキーマハッシュをコンパイル時に計算
- 実行時のリフレクションを排除
- レイアウトの検証はすべてコンパイル時

### 2. データ指向

- `Entity` 参照は **pid（uint）** として保存
- アセット参照は **安定 ID（ulong）** として保存
- 「参照」ではなく「識別子」を保存する

### 3. フェーズ分離

Deserialize は 20 段階の Phase に分割され、各 Phase が独立して検証・実行されます：

```
D0〜D8.5: Prephase（検証、Migration、シーン状態解析）
P0〜P8  : Apply（Entity 生成、モジュール適用、確定）
```

各 Phase の責務が明確で、失敗時のロールバック範囲も限定されます。

### 4. 診断の重視

- コンパイル時に型制約を検証（`ECSAVE001-065`）
- 実行時に危険パターンを警告（`LoadWarningKind`）
- エラーには **説明 + 対処法** を付与（`GetUserFacingMessage`）

---

## 既知の制約

| 制約 | 内容 | 回避策 |
|---|---|---|
| `IComponentMigration` の Entity 非対応 | `Entity` / `FixedString` / `FixedList` を含む型は移行不可 | `IPidMigration` を使う |
| `WeakObjectReference<T>` の直接保存 | ビルド間で参照が壊れる | `AssetReferenceRegistry` で安定 ID に変換 |
| メインスレッド専用 | 公開 API はすべてメインスレッド専用 | `MainThreadDetector.ThrowIfNotMainThread` で検出 |
| `FixedList<Entity>` 非対応 | FixedList の要素に `Entity` を含められない | `DynamicBuffer<T>` を使う |
| World 破棄後の使用 | 破棄後の World に対する操作は未定義 | `World.IsCreated` を確認 |

---

## FAQ

### Q. `ECSAVE030` が出る

`IComponentMigration<TOld, TNew>` の `TOld` / `TNew` が `Entity` / `FixedString` / `FixedList` / ジェネリック型を含んでいます。`IPidMigration` を使ってください。

### Q. セーブデータのサイズを小さくしたい

- LZ4 圧縮は自動（256B 以上）
- 大きい `FixedString` は短くする
- 不要なモジュールを `Register` しない
- `DynamicBuffer` の要素数を制限

### Q. `ConcurrentOperation` が返る

同じ World に対して同時に `Serialize` / `Deserialize` を呼んでいます。`SaveRuntimeSystem` が `WorldOperationGuard` で排他制御しています。

### Q. セーブデータのバージョン管理は？

- `GameDataVersion(int)` でゲームデータのバージョンを記録
- `FormatVersion`（フォーマット層）は別管理
- `CurrentFormatVersion` より新しいデータは読めない

### Q. Migration が走らない

- `RegisterMigration<T>` を呼んだか
- `RenamedFrom` が必要か（型名を変えた場合）
- `ECSAVE065` / `ECSAVE030` などの警告が出ていないか
- `MigrationTable` が `SaveDefinition.Migrations` に設定されているか

### Q. World を破棄したとき、保留中の操作は？

`SaveRuntimeSystem.OnDestroy` で `Operation.OnAbort` が呼ばれ、`WorldModificationFailed` / `SerializationFailed` で完了します。

---

## ライセンス

（プロジェクトに合わせて記載）

---

## コントリビューション

Issue / Pull Request を歓迎します。詳細は `CONTRIBUTING.md` を参照してください。

---


