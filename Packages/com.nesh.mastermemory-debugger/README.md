# MasterMemory Runtime Debugger

`com.nesh.mastermemory-debugger`

[Cysharp/MasterMemory](https://github.com/Cysharp/MasterMemory) v3 的 Runtime 資料查閱與開發用 Override 工具，UI 完全使用 **UI Toolkit**。

- 在 Unity Editor / Development Build 中瀏覽、搜尋所有 MasterMemory Table
- 對既有 Record 的非 Key 欄位建立 **Runtime Override**（MasterMemory 本體維持 Immutable）
- 將修改過的欄位存成 / 匯出為 JSON Patch，重開遊戲後可載入，也能交給企劃回填主資料

> 本 Package 的定位是 **Development Runtime Inspector + Value Override Tool**，不是 Runtime Database Editor。
> 不支援新增 / 刪除 / 複製 Record、修改 PrimaryKey / SecondaryKey、Schema 變更。
> 原始 MemoryDatabase 永遠不會被修改；需要時可以用 `MasterMemoryDebugRebuild` 產生一份套用了 Override 的新 database（選用）。

---

## Requirements

| 項目 | 版本 |
| --- | --- |
| Unity | 6000.0 以上（實測版本：6000.6） |
| MasterMemory | 3.x（透過 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) 安裝） |
| MessagePack | MasterMemory 的相依套件（NuGetForUnity 會一起安裝） |
| Input System | 選用。有安裝就使用 Input System，否則使用舊的 Input Manager |

## Installation

### 1. NuGetForUnity

Package Manager → `+` → **Add package from git URL...**

```
https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity
```

### 2. MasterMemory

選單 **NuGet > Manage NuGet Packages** → 搜尋 `MasterMemory` → 安裝 3.x。
MessagePack、MasterMemory.Annotations 等相依套件會一起安裝。

> 如果是手寫 `Assets/packages.config` 再 Restore，必須列出所有相依套件
>（MasterMemory、MasterMemory.Annotations、MessagePack、MessagePack.Annotations、MessagePackAnalyzer、
> Microsoft.NET.StringTools、System.Collections.Immutable），否則 Console 會出現
> `MasterMemory.dll will not be loaded ... Unable to resolve reference 'MessagePack'`。

MasterMemory 本身的必要設定（放 Master 定義的 assembly 中任一個 `.cs`）：

```csharp
[assembly: MasterMemoryGeneratorOptions(Namespace = "MyGame.MasterData")]

namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit { }
}
```

IL2CPP 還需要把產生的 `MasterMemoryResolver` 註冊到 MessagePack（參考 MasterMemory README）。

### 3. 本 Package

Package Manager → `+` → **Add package from git URL...**

   ```
   https://github.com/NeshGames/mastermemory-debugger.git?path=/Packages/com.nesh.mastermemory-debugger#v0.7.0
   ```

   URL 最後的 `#v0.7.0` 鎖定版本（建議）；拿掉則會安裝 `main` 的最新內容。各版本見 [Releases](https://github.com/NeshGames/mastermemory-debugger/releases) 與 `CHANGELOG.md`。

Runtime assembly (`Nesh.MasterMemoryDebugger.Runtime`) 會自動參考 NuGetForUnity 安裝的 `MasterMemory.dll`。

## Package Architecture

```text
Gameplay
   ↓
Project MasterDataService ── MasterMemoryDebugRuntime.TryGetOverride()
   │                              ├─ Found     → Runtime Override (clone)
   │                              └─ Not Found → MasterMemory (immutable)
   ↓
MemoryDatabase

UPM Package
 ├─ Registry          MasterMemoryDebugRegistry（由專案註冊 Table）
 ├─ Reflection Cache  MasterDataReflectionCache（第一次遇到型別時掃描一次）
 ├─ Override Store    MasterDataOverrideStore
 ├─ Patch             MasterDataPatchService / Serializer / Storage / Exporter
 ├─ UI Toolkit        RuntimeMasterMemoryDebugger + Controllers + UXML/USS
 └─ Settings          MasterMemoryDebuggerSettings（Project Settings）
```

Package 不會直接接管 Gameplay 的資料存取，也不會修改 MasterMemory 的 table / index / record instance。
編輯永遠是在 Clone 出來的副本上進行：`Original → Clone → Editable Copy → Override Store`。

## Quick Start

1. 安裝 MasterMemory
2. 安裝本 Package
3. 註冊 Table
4. 在專案的 MasterDataService 加入 `TryGetOverride`
5. 進入 Play Mode（或執行 Development Build）
6. 按 **F8**

Package Manager 的 **Samples → Basic Example** 有完整範例：匯入後在任一場景的 GameObject 加上 `ExampleDebuggerLauncher`，然後按 Play。

## Register Table

### 一次註冊整個 MemoryDatabase（推薦）

使用 MasterMemory 產生的 `GetMetaDatabase()` / `GetTable()`，PrimaryKey（含複合主鍵）和 SecondaryKey 都會自動判斷：

```csharp
// 先設定版本：會寫進 Patch，載入時也會拿來比對
MasterMemoryDebugRegistry.SetMasterVersionProvider(() => currentMasterVersion);

MasterMemoryDebugRegistry.RegisterDatabase(
    MemoryDatabase.GetMetaDatabase(),
    tableName => MemoryDatabase.GetTable(originalDatabase, tableName));
```

`getTable` 每次讀取資料時都會重新呼叫，所以重新載入 database 後不需要重新註冊。
它必須回傳 **原始** database 的 table（Debugger 會拿它來比對 Original / Current）。

### 手動註冊單一 Table

```csharp
MasterMemoryDebugRegistry.RegisterTable<SkillMaster, int>(
    tableName: "SkillMaster",
    getAllRecords: () => database.SkillMasterTable.All,
    getPrimaryKey: x => x.Id,
    getDisplayName: x => x.Name);
```

### Table 群組

Table 多的時候，可以在 Table 列表用資料夾分群（可折疊）。名稱可以用註冊的 Table 名稱（`RegisterDatabase` 時是 record 類別名稱）或 `[MemoryTable]` 名稱：

```csharp
MasterMemoryDebugRegistry.SetTableGroup("Battle",
    "CharacterMaster", "MonsterMaster", "SkillMaster", "EffectMaster", "ItemMaster");
MasterMemoryDebugRegistry.SetTableGroup("Economy", "ShopMaster");

// 也可以用型別指定（優先於名稱）
MasterMemoryDebugRegistry.SetTableGroup<GachaMaster>("Economy");
```

- 群組依第一次設定的順序排列，群組內的 Table 依名稱排序。
- 沒有指定群組的 Table 會放在最後的 `Other`；完全沒有設定群組時顯示成一般列表。
- 可以在註冊 Table 之前或之後呼叫。
- 點群組列可以展開 / 收合。

### 其他設定

```csharp
// Record List 顯示名稱（預設使用 Name / DisplayName / Title / Label 或第一個 string 欄位）
MasterMemoryDebugRegistry.SetDisplayName<EnemyLevelMaster>(x => $"Enemy {x.EnemyId} Lv.{x.Level}");

// 自訂 Clone（預設是淺層 member-wise clone，一般情況下就夠用）
MasterMemoryDebugRegistry.RegisterCloneProvider<SkillMaster>(x => x with { });
```

所有註冊 API 在非 Editor / 非 Development Build 中都不會做任何事。

### 顯示名稱與 Tips（多語言）

Table 與欄位可以設定各語言的顯示名稱和提示（Tips）。標題列的語言下拉選單可以在「Code names」（程式名稱）與各語言之間切換（記在 PlayerPrefs）。
顯示名稱會用在 Table 清單、釘選頁籤、表格標題、Inspector 與搜尋自動完成；程式名稱仍會出現在 Tooltip 裡，搜尋條件與 Patch 也一律使用程式名稱。

```csharp
MasterMemoryDebugLocalization.SetTableLabel<SkillMaster>("zh-TW", "技能", "所有技能的基本數值");
MasterMemoryDebugLocalization.SetFieldLabel<SkillMaster>("Damage", "zh-TW", "傷害", "基礎傷害，未含角色加成");
// 語言給 null：這個 Tip 在所有語言（包含 Code names）都會顯示
MasterMemoryDebugLocalization.SetFieldLabel<SkillMaster>("Cooldown", null, null, "單位：秒");

// 或從試算表匯出的 Tab 分隔文字一次載入：table, field（Table 本身留空）, language, label, tip
MasterMemoryDebugLocalization.LoadTsv(labelsTextAsset.text);
```

```text
table	field	language	label	tip
SkillMaster		zh-TW	技能	所有技能的基本數值
SkillMaster	Damage	zh-TW	傷害	基礎傷害\n未含角色加成
```

- Table 名稱可以用註冊名稱（`RegisterDatabase` 時是 Record 類別名稱）或 `[MemoryTable]` 名稱。
- Tip 中的 `\n` 會換行；`#` 開頭的行、label 與 tip 都空白的行會被略過。
- **範本**：Debugger 的 Tables 標題旁 **Labels TSV** 會把所有 Table 與欄位名稱複製成上面的格式（已設定的名稱與 Tip 會一併填入，
  語言欄是目前選擇的語言；選 Code names 時留空）。貼到試算表填好後，存成文字交給 `LoadTsv`。程式中：`MasterMemoryDebugLocalization.CreateTsvTemplate("zh-TW")`。
- 中文、日文等名稱需要字型支援：在 Settings 的 **Font** 指定含有這些字的字型（例如 Noto Sans TC），否則會顯示成方框。

## Integrate Override

```csharp
public SkillMaster GetSkill(int id)
{
    if (MasterMemoryDebugRuntime.TryGetOverride<SkillMaster, int>(id, out var value))
    {
        return value;
    }
    return _database.SkillMasterTable.FindById(id);
}

// 或使用 helper
public ItemMaster GetItem(int id)
    => MasterMemoryDebugRuntime.Resolve<ItemMaster, int>(id, key => _database.ItemMasterTable.FindById(key));

// 複合主鍵是 ValueTuple，和 FindByXxxAndYyy 相同
public EnemyLevelMaster GetEnemyLevel(int enemyId, int level)
    => MasterMemoryDebugRuntime.Resolve<EnemyLevelMaster, (int, int)>(
        (enemyId, level), key => _database.EnemyLevelMasterTable.FindByEnemyIdAndLevel(key));
```

沒有任何 Override 的型別不會進行 dictionary 查詢，也不會 boxing。

### （選用）重建 Gameplay Database：`MasterMemoryDebugRebuild`

如果希望 SecondaryKey / Range / `All` 查詢也能讀到 Override，讓遊戲端讀取的 database 參考換成「套用了 Override 的新 database」。只要一行：

```csharp
// 遊戲端透過 masterService.Database 讀取主資料
var rebuild = MasterMemoryDebugRebuild.AutoRebuild(originalDatabase, db => masterService.Database = db);
// 不再需要時：rebuild.Dispose();  → 換回原始 database
```

- 內部使用 MasterMemory 官方的 `ToImmutableBuilder().Diff(records).Build()`（透過 reflection 找到每張表的 `Diff`，不需要手寫）。
- 每次 Override 變更都會從**原始** database 重建，所以 Reset 會回到原始值；沒有 Override 時直接給原始 database。
- **驗證**：如果 Record 有實作 MasterMemory 的 `IValidatable<T>`，每次重建後會執行 `Validate()`，
  並把「原始資料沒有、Override 之後才出現」的失敗寫進 Log（例如把 `StartSkillId` 改成不存在的 99），
  所有失敗也會列在 Debugger 的 **Validation** 頁籤（見下方）。可用 `validate: false` 關閉。
- 只想重建一次：`var db = MasterMemoryDebugRebuild.Apply(originalDatabase);`
- 正式版 Build 中 `AutoRebuild` 只會呼叫一次 `apply(originalDatabase)`，不會訂閱任何事件。

代價：每次變更都會重建並重新排序有 Override 的表；其他地方持有的舊 database / record 參考不會更新。範例見 `Samples~/BasicExample/ExampleDebuggerLauncher.cs`。

## UI Toolkit Runtime Debugger

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ MasterMemory Debugger [Data][Changes (2)][Patches (3)][Validation (1 new)] Master: v1 [zh-TW ▼][Close] │
├────────────┬┬─────────────────────────────────────────────┬┬──────────────────────────┤
│ Tables [TSV]││ [技能 ×][武器 ×] [+ Pin]                      ││ 技能 1001  Overridden     │
│ ▼ Battle(6)││ [Damage>100..] [ ]Mod [Columns▾][Batch Edit…][Copy]││ Id  PK             │
│   技能     ││ ● │ Id (PK)┃ 分類 (SK)│ 名稱    │ 傷害 ▼│ ...   ││ 1001                     │
│   武器     ││   │   1004 ┃        1 │ Thunder │   180 │       ││ 傷害       Original: 120 │
│ ▶ Economy  ││ ● │   1001 ┃        1 │ Fireball│   185 │       ││ [185                   ] │
│            ││ 2 / 2005 records                ◀━━━━━━━━▶   ││ ▼ Referenced by           │
│            ││                                             ││   角色.初始技能 3 [Show] │
│            ││                                             ││ [Apply][Revert][Reset]   │
├────────────┴┴─────────────────────────────────────────────┴┴──────────────────────────┤
│ status...                                                   [↶ Undo][↷ Redo][Log (12)] │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

- 標題列的頁籤：**Data**（瀏覽與編輯）、**Changes**（所有修改的總覽）、**Patches**（Patch 管理，見下方）、**Validation**（MasterMemory 驗證結果）。
- Data 頁的版面和一般資料庫檢視工具相同：左側 Table 清單、中間資料表格、右側 Record 詳細資料；兩條分隔線都可以拖曳調整寬度。

### Record 表格

- 第一欄 `●` 標示有 Override 的 Record，接著是主鍵欄位（`(PK)`，複合主鍵每個成員一欄），然後每個欄位一欄（SecondaryKey 標 `(SK)`，複雜型別顯示預覽）。
- 專案用 `SetDisplayName` / `RegisterTable(getDisplayName)` 提供顯示名稱時，會多一欄 `Display`；預設的顯示名稱只是重複某個欄位，所以不另外顯示。
- 數字靠右對齊、`null` 顯示為灰色的 `NULL`、有格線與交錯底色。有 Override 的格子若和原始值不同，會以橘色顯示。
- **凍結欄位**：凍結的欄位固定在左側（藍色分隔線左邊），其他欄位可以水平捲動（下方捲軸，或 Shift + 滾輪 / 觸控板左右滑動）。
  `●` 與主鍵欄位**一律顯示並凍結**，不能取消。
- **Columns ▾**：其他欄位可以勾選 **Show**（顯示 / 隱藏）與 **Freeze**（凍結）；`Show All` 全部顯示、`Reset` 回到預設。
- **欄寬自動調整**：依欄位名稱與前 200 筆的內容估算寬度（40～320px，太長的內容以 `…` 截斷，完整內容看右側 Inspector）。
  拖曳欄位標題的右邊界可以手動調整；**雙擊**右邊界回到自動寬度。
- 點欄位標題排序（遞增 → 遞減 → 不排序）。排序會套用在所有符合條件的資料上，再取前 `Max Search Results` 筆（預設 500）。
- 欄位的顯示、凍結與手動寬度會依 Table 記在 PlayerPrefs，重新執行後仍會保留（`Reset` 回到預設）。
- **Batch Edit…**：對「符合目前搜尋條件的所有 Record」（不只畫面上顯示的前 500 筆）一次修改一個欄位，結果存成 Override：
  - **Set**：全部設成同一個值（數字、文字直接輸入；enum 與 bool 從下拉選單選擇；Flags 用 `|` 連接；Nullable 欄位可以輸入 / 選擇 `null`）
  - **Add**：數字加上一個值（負數為減少），例如 `Price + 100`
  - **Multiply**：數字乘上一個值，例如 `Damage × 1.1`；整數以四捨五入（.5 遠離 0）取整
  - 主鍵 / SecondaryKey、Array / List 與複雜型別不能批次修改。改完後所有值都和原始值相同的 Record 會移除 Override。
  - 無法修改的 Record（值是 null、超出型別範圍）會略過並列在 Console；整次修改是**一個 Undo 步驟**。
  - 程式中：`MasterMemoryBatchEdit.Apply(records, field, MasterMemoryBatchOperation.Multiply, "1.1")`。
- **Copy**：把目前顯示的資料列與欄位（套用搜尋、排序、欄位顯示設定後的結果，凍結欄位在前）複製成 Tab 分隔文字，可以直接貼到 Excel / Google 試算表。WebGL 會下載成 `.tsv`。
- 範例的 `ExampleManyColumnsMaster`（75 欄、120 筆，Test 群組）與 `ExampleWeaponMaster`（27 欄）可以用來確認超出一個畫面時的水平捲動；
  `ExampleLargeMaster`（50,000 筆，Test 群組）用來確認大表的開啟、捲動、搜尋、排序與 Batch Edit 的反應速度。
- **大表**：表格只顯示符合條件的前 `Max Search Results` 筆（預設 500），搜尋、排序、Batch Edit、Copy TSV 等則對整張表運作。
  5 萬筆時這些操作在 .NET 上都在 1 秒內（`Tests/Runtime/LargeTableTests` 會印出各項耗時；Unity 的 Mono / IL2CPP 通常慢數倍）。
- **釘選頁籤**：表格上方的 `+ Pin` 把目前的 Table 釘選成頁籤，點頁籤快速切換，`×` 取消釘選。釘選清單記在 PlayerPrefs，下次開啟仍會保留。
- 表格是自己實作的 virtualized grid（`ListView` + 同步捲動的表頭），沒有使用 `MultiColumnListView`。

### 搜尋 / 篩選

以空白分隔多個條件，**全部符合**才會顯示：

| 寫法 | 意義 |
| --- | --- |
| `ice` | PrimaryKey / Display Name / string 欄位包含 `ice`（不分大小寫） |
| `Damage>100` | 欄位比較，運算子 `=` `!=` `>` `>=` `<` `<=` |
| `Name~blast` | 欄位文字包含 |
| `Element=Fire` | enum 用名稱（不分大小寫） |
| `IsPassive=true` | bool |
| `Name="Ice Blast"` | 值有空白時加引號 |
| `UnlockLevel=null` | null |
| `Category=1 Damage > 100` | 多個條件（運算子前後可以有空格） |

欄位名稱不分大小寫；比較的是目前值（有 Override 時用 Override）。欄位不存在或值格式錯誤時，筆數旁會顯示警告並忽略該條件。
只在查詢、Table、排序、Override 變更時重新計算（輸入時有 150ms debounce）。

**自動完成**：輸入時搜尋框下方會出現候選清單，**↑ / ↓** 選擇，**Tab / Enter** 或點擊套用，**Esc** 關閉清單（不會關閉 Debugger）。

- 欄位名稱：輸入 `da` → `Damage`。沒有開頭符合的欄位時，改用「包含」比對（`mag` → `Damage`）。
- 運算子後的值：enum 名稱與 `true` / `false`（`Element=f` → `Fire`；Flags 可用 `|` 連接：`Flags=Boss|Fl` → `Flying`）。
- 欄位名稱打完整時，清單會列出可用的運算子。
- 焦點在搜尋框時，Tab 不會跳到下一個控制項。
- **最近的搜尋**：搜尋框是空的時，清單會列出最近 10 個搜尋條件（有文字時按 **↓** 會列出包含該文字的條件）。
  按 Enter、離開搜尋框或關閉 Debugger 時，沒有錯誤的搜尋條件會被記下（PlayerPrefs）。

### Inspector

- 列出所有 public property / field：
  - `PK` / `SK`：永遠唯讀
  - 支援編輯：`int uint short ushort long ulong byte sbyte float double bool string enum`、`[Flags] enum`（以文字輸入）、`Vector2 Vector3 Vector2Int Vector3Int Color`、以及上述型別的 `Nullable<T>`
  - **Array / List**（`T[]`、`List<T>`、`IReadOnlyList<T>` 等，元素為上述簡單型別）：逐項編輯、`×` 刪除、`+ Add` 新增（複製最後一項）。
    每次修改都會建立新的陣列 / List，原始 Record 與已套用的 Override 不會被改到；Patch 會把整個 List 存成 JSON 陣列。超過 200 項時唯讀。
  - Dictionary / 巢狀物件 / 元素為複雜型別的 List：唯讀的可折疊樹狀檢視（最多 3 層、每層最多 100 項，展開時才建立）
  - 有修改的欄位會顯示 `Original: xxx`
  - 文字欄位會自動換行並長高，完整顯示很長的值（Enter 仍然是 Apply，不會插入換行）
  - **關聯跳轉**：Record 有實作 MasterMemory 的 `IValidatable<T>` 並用 `GetReferenceSet<T>().Exists(x => x.ItemId, y => y.Id)` 宣告關聯時，
    該欄位旁會出現 `→ ItemMaster` 按鈕，點擊會開啟被參照的 Record（參照的不是主鍵時，改為以 `Id=值` 篩選目標 Table）。
    不需要額外設定：關聯是從 `Validate` 的 `Exists()` 讀出來的（`MasterMemoryReferences.Get(table)`）；沒有寫 Validate 的 Record 就不會顯示按鈕。
  - **Referenced by**（反向關聯）：Inspector 最下方列出參照這筆 Record 的其他 Table 欄位與筆數（使用目前值，包含 Override），
    **Show** 會開啟來源 Table 並以 `StartSkillId=1001` 篩選。修改或刪減資料前可以先確認影響範圍。
    區塊可以折疊（折疊時不計算）；程式中可用 `MasterMemoryReferences.GetIncoming(table)` / `FindReferencing(reference, value)`。
- **Apply** 會把編輯中的副本存進 Override Store；如果所有值都和原始值相同，會改為移除 Override。
- **Copy JSON**：把整筆 Record（包含陣列與巢狀物件、未套用的編輯）複製為 JSON。Editor / Windows 複製到剪貼簿，WebGL 下載成檔案。
- 有未套用的編輯時切換 Record / Table，會詢問 **Apply / Discard / Cancel**。

### Changes（修改總覽）

標題列的 **Changes (N)** 會把主畫面切換成所有 Override 的總覽：每筆 Record 的修改欄位（原始值 → 目前值），可以 **Open**（跳到該筆 Record）或 **Reset**。
原始 Record 已不存在（例如主資料刪掉了）或 Table 沒有註冊的 Override 會以紅色標示。程式中可用 `MasterMemoryChangeSummary.Build()` 取得同樣的資料。

**Copy TSV** 會把所有修改過的欄位複製成 `table / key / name / field / original / current` 的 Tab 分隔表格，
可以貼到試算表，對照著把調整好的數值回填到主資料的原始檔（WebGL 下載成 `changes.tsv`；程式中：`MasterMemoryChangeSummary.ToTsv(...)`）。

**Paste TSV…** 是反方向：把在試算表裡改好的表格貼回來，變成 Override。

1. Copy TSV → 貼到試算表 → 修改 `current` 欄（也可以新增列：填 table、key、field、current 即可）。
2. 全選複製 → Paste TSV… → 貼上 → **Preview**：列出會改變的值（原值 → 新值）、無法匯入的行與原因。
3. **Apply** 套用（一個 Undo 步驟）。

- 第一行必須是欄位名稱，需要 `table`、`key`、`field`、`current`（或 `value`），順序不限，其他欄位會被忽略。
- `key` 的寫法和 Debugger 顯示的相同（`1001`、複合主鍵 `(2, 1)`）。主鍵、List 與複雜型別不能匯入。
- 有 `original` 欄時，會提醒「原始值已經和匯出時不同」的行（主資料更新過），但仍然會匯入。
- 程式中：`var plan = MasterMemoryTsvImport.Read(text); MasterMemoryTsvImport.Apply(plan);`

### Undo / Redo

狀態列的 **↶ Undo / ↷ Redo**（**Ctrl+Z / Ctrl+Y**，macOS 為 Cmd；Ctrl+Shift+Z 也是 Redo）可以復原 Debugger 做的修改：
Inspector 的 Apply / Reset、Changes 的 Reset、Reset All、套用 / 合併 Patch、Batch Edit，每個操作是一個步驟（最多 50 步）。
按鈕的 Tooltip 會顯示下一個要復原的操作。

- Undo 只會把受影響的 Record 恢復成操作前的 Override（或沒有 Override），不會動到其他 Record。
- 遊戲程式直接呼叫 `SetOverride` 等 API 修改 Override 時，歷史紀錄會清空（避免 Undo 覆蓋掉程式的修改）。
- 焦點在搜尋框時 Ctrl+Z 不會觸發 Undo。
- 程式中：`using (MasterMemoryDebugHistory.Record("說明")) { ... }` 把自己的修改記成一個步驟，`MasterMemoryDebugHistory.Undo()` / `Redo()`。

### Validation（驗證結果）

使用 `MasterMemoryDebugRebuild.AutoRebuild(...)`（預設 `validate: true`）時，**Validation** 頁籤會列出重建後 database 的所有 `Validate()` 失敗：

- Override 造成的失敗（原始資料沒有）標示為紅色 **NEW** 並排在最前面；頁籤標題會顯示 `Validation (N new)`。
- **Open** 跳到出問題的 Record；**New only** 只顯示 Override 造成的失敗；**Validate** 重新執行。
- 只在頁籤顯示時執行驗證（原始資料的驗證只做一次並快取）。沒有使用 AutoRebuild 時會顯示設定說明。
- 程式中：`MasterMemoryDebugValidation.Run()` 取得同樣的清單，`NewFailureCount` 取得最近一次重建新增的失敗數。

### Log 與 Console

- 狀態列右側的 **Log (N)** 會展開最近 50 則訊息（狀態、Patch 警告、修改內容），在沒有 Console 的實機上也看得到。
- Apply / Reset / Reset All / 套用 Patch 都會在 Console 印出修改內容（可在 Settings 關閉）：

  ```text
  [MasterMemoryDebugger] Override applied: SkillMaster 1001 (Fireball)
    Damage: 120 → 185
    Cooldown: 2.5 → 1.8
  ```

  Editor Console 中標題為橘色、欄位名稱為黃色、舊值灰色、新值綠色；Development Build 的 log 檔不含顏色標籤。
  專案也可以用 `MasterDataDiffUtility.GetChanges(before, after)` / `Format(...)` 產生同樣的比對結果。

### 快捷鍵與其他

| 按鍵 | 行為 |
| --- | --- |
| F8 | 開關 Debugger（可在 Settings 修改） |
| 三指長按 1 秒 | 觸控裝置上開關 Debugger（手指數與秒數可在 Settings 修改，0 指停用） |
| Enter | Inspector 中：Apply；對話框中：執行主要按鈕（刪除 / 覆蓋 / Reset All 等危險操作不會被 Enter 觸發） |
| ↑ / ↓、Tab / Enter | 搜尋框自動完成：選擇、套用 |
| Ctrl+Z / Ctrl+Y（Ctrl+Shift+Z） | Undo / Redo（macOS 為 Cmd）；焦點在搜尋框時不作用 |
| Esc | 依序關閉：對話框、自動完成清單、Columns 清單；都沒有時關閉 Debugger |
| Shift + 滾輪 | 表格水平捲動 |

- **Reset All** 會先跳出確認視窗：`Reset all MasterMemory runtime overrides?`
- `A-` / `A+` 可調整 UI 縮放（只在使用 Debugger 自己建立的 PanelSettings 時顯示）。

Runtime 的 UI Toolkit 沒有 `ColorField`、`ToolbarSearchField`、`EnumFlagsField`，所以 Color 使用 RGBA 四個 `FloatField`，搜尋框使用 `TextField`，Flags enum 使用文字輸入。

## Open / Close / Toggle

其他專案（或遊戲自己的 Debug Menu / 按鈕 / 指令）可以直接用 API 開關 Debugger：

```csharp
RuntimeMasterMemoryDebugger.Open();    // 開啟；無法開啟時回傳 false（正式版 Build、Settings 停用、非 Play Mode）
RuntimeMasterMemoryDebugger.Close();
RuntimeMasterMemoryDebugger.Toggle();
bool isOpen = RuntimeMasterMemoryDebugger.IsOpen;
bool canOpen = RuntimeMasterMemoryDebugger.IsAvailable;  // 例如用來決定 Debug Menu 要不要顯示按鈕

// 開關時通知（例如開啟時暫停遊戲、關閉遊戲自己的輸入）
RuntimeMasterMemoryDebugger.OpenStateChanged += isOpen => Time.timeScale = isOpen ? 0f : 1f;
```

- 預設熱鍵 **F8**（Settings 的 `Toggle Key`），支援 Input System 與舊的 Input Manager。
- 觸控裝置：預設**三指長按 1 秒**開關（Settings 的 `Touch Toggle Fingers` / `Touch Toggle Seconds`）。
- 只想由自己的 UI 開關時，在 Settings 把 `Toggle Key` 設為 `None`、`Touch Toggle Fingers` 設為 `0`，內建的熱鍵與手勢就會停用。
- 正式版 Build 中這些 API 都不會做任何事，呼叫端不需要 `#if`。
- UI 採 Lazy Create：開啟時才建立 `MasterMemoryRuntimeDebugger` GameObject + `UIDocument`，關閉時整個銷毀。關閉狀態下只有熱鍵 listener 存在，不會做任何 Reflection、UI 更新或 List refresh。
- Editor 選單：`Tools > MasterMemory Debugger`。

## Patch Save / Load / Export

Patch 只記錄 **有修改的可編輯欄位** 與其 **原始值**，不會保存整個 database：

```json
{
  "formatVersion": 1,
  "masterVersion": "2026.09.26.001",
  "exportedAt": "2026-09-26T14:00:00Z",
  "tables": [
    {
      "tableName": "SkillMaster",
      "memoryTableName": "skill",
      "recordType": "MyGame.SkillMaster",
      "records": [
        {
          "primaryKey": { "Id": 1001 },
          "changes": [
            { "field": "Damage", "original": 120, "value": 185 },
            { "field": "Cooldown", "original": 2.5, "value": 1.8 }
          ]
        }
      ]
    }
  ]
}
```

- 複合主鍵：`"primaryKey": { "EnemyId": 1, "Level": 2 }`
- enum 用名稱字串，`Vector2/3` 用 `{"x","y","z"}`，`Color` 用 `{"r","g","b","a"}`，`long / ulong` 不會失去精度。
- 各專案可以依自己的主資料來源（Excel / Google Sheets / CSV …）處理這個 JSON；也可以在程式裡直接取得 DTO：

  ```csharp
  MasterDataPatch patch = MasterDataPatchService.CreatePatch();
  string json = MasterDataPatchSerializer.ToJson(patch);
  ```

Patch 可以命名，存成 `Application.persistentDataPath/MasterMemoryDebugger/<名稱>.patch.json`。所有 Patch 操作都在 **Patches** 頁籤：

```text
┌ Current: 3 overridden records  [Save As…][Export][Reset All]              [Import][Open Folder] ┐
├ Saved patches (3)          ┃ balance-A                                                          │
│ balance-A                  ┃ 12 records, 30 fields · master version v1 · saved 2026-09-26 14:00 │
│  12 records · 30 fields    ┃ [Apply][Merge][Overwrite][Rename…][Export][Delete]                 │
│ debug  (default)           ┃ SkillMaster (2)                                                    │
│  3 records · 5 fields      ┃  {"Id":1001}   Damage  120 → 185                                   │
└────────────────────────────┴────────────────────────────────────────────────────────────────────┘
```

| 操作 | 行為 |
| --- | --- |
| Save As… | 把目前的 Override 存成新的 Patch（輸入名稱；同名時確認是否覆蓋）。不能用在檔名的字元會換成 `_` |
| Export（上方） | 匯出目前的 Override（檔名 `current-<時間>.json`）。Editor：存檔對話框；**WebGL：瀏覽器下載**；其他平台：`.../MasterMemoryDebugger/exports/` |
| Reset All | 移除所有 Override（會先確認） |
| Import | 把 Patch 檔加入清單。Editor：檔案對話框；**WebGL：瀏覽器上傳**；其他平台：貼上 JSON。同名時確認是否覆蓋 |
| Open Folder | 開啟資料夾（Editor / Windows / macOS / Linux） |
| 清單 | 每個 Patch 的筆數、欄位數、儲存時間；版本和目前主資料不同的會以橘色標示。右側預覽每個欄位的原始值 → 修改值 |
| Apply | **取代**目前所有 Override（目前有 Override 時會先確認） |
| Merge | 疊加在目前的 Override 上（同一筆 Record 會被 Patch 的內容取代） |
| Overwrite | 用目前的 Override 覆蓋這個 Patch |
| Rename… / Export / Delete | 改名、匯出這個 Patch、刪除（目前的 Override 不受影響） |

程式碼中也可以直接使用：`MasterDataPatchStorage.Save(patch, "balance-A")`、`Load("balance-A")`、`ListPatchNames()`、`Delete("balance-A")`。

載入時：

- Master Version 不同 → 顯示警告，可選 **Cancel** 或 **Force Apply**。
- Record 不存在 → 略過（不能新增 Record）。
- Key 欄位或不存在的欄位 → 略過。
- `original` 和目前主資料不同 → 仍然套用，但會列出警告（代表主資料已經改過）。
- `Auto Load Patch` 開啟時，會在 Table 註冊時自動套用 `Default Patch Name` 的 Patch；版本不同時不會自動套用。

## Settings

`Project Settings > MasterMemory Debugger`（會在 `Assets/MasterMemoryDebugger/Resources/MasterMemoryDebuggerSettings.asset` 建立設定檔）

| 設定 | 預設 | 說明 |
| --- | --- | --- |
| Enabled | true | 關閉時 UI、熱鍵、Auto Load 全部停用（Override API 仍可使用） |
| Allow Editing | true | 關閉時變成唯讀瀏覽器 |
| Allow Patch Save | true | Patches 頁籤顯示 Save As / Overwrite / Rename / Export / Delete |
| Auto Load Patch | false | 註冊 Table 時自動載入已儲存的 Patch |
| Toggle Key | F8 | |
| Touch Toggle Fingers | 3 | 觸控裝置上幾根手指同時按住會開關 Debugger；0 = 停用 |
| Touch Toggle Seconds | 1 | 需要按住的秒數 |
| Max Search Results | 500 | |
| Show Secondary Keys | true | 在 Inspector 顯示 SecondaryKey 欄位（永遠唯讀） |
| Log Level | Warning | |
| Log Override Changes | true | Apply / Reset / Reset All / 套用 Patch 時在 Console 列出改了哪些欄位與前後值（Editor 中以顏色標示） |
| Default Patch Name | debug | Patches 頁籤中標示為 (default) 的 Patch，也是 Auto Load Patch 載入的 Patch |
| Font | (none) | Debugger UI 使用的字型（TTF / OTF）。顯示名稱使用中文、日文等文字時，請指定含有這些字的字型 |
| Panel Settings | (none) | 指定專案自己的 PanelSettings；沒指定時 Debugger 會自行建立 |
| Sorting Order | 10000 | Debugger 自行建立的 Panel 的繪製順序 |

## Development Build Behavior

所有功能只在 Unity Editor 與 Development Build 中啟用。Player 在啟動時（任何場景載入前）讀取一次 `Debug.isDebugBuild` 來判斷，
不使用 Unity 6.6 起已 deprecated 的 `DEVELOPMENT_BUILD` define。正式版 Build 中：

- `TryGetOverride` 永遠回傳 `false` → 讀取 MasterMemory
- `SetOverride` / `RegisterTable` / `RegisterDatabase` 等 API 不做任何事
- Debugger UI 不會開啟，熱鍵 listener 不會建立，Patch 不會載入

呼叫端不需要加 `#if`。如果連 Development Build 都要完全停用，在 Scripting Define Symbols 加上 `MMDEBUGGER_DISABLE`。

## PrimaryKey Limitation

PrimaryKey 永遠唯讀，不能被 Override，也不能透過 Patch 修改。

## SecondaryKey Limitation

SecondaryKey 欄位也永遠唯讀。MasterMemory 的 Secondary Index 不會跟 Override Layer 同步：
就算把 `Category` 從 2 改成 3，`FindByCategory(3)` 仍然查的是原始 index，所以一律禁止修改。

## Query Limitation

Override Layer **只保證經過 `TryGetOverride` / `Resolve` 的 PrimaryKey 查詢** 會拿到修改後的值。
以下查詢 **不會** 反映 Override：

- SecondaryKey 查詢（`FindByXxx` 非主鍵）
- Range 查詢（`FindRangeByXxx`、`FindClosestByXxx`）
- `All` / `SortByXxx`
- 遊戲端已經拿在手上的 Record 參考

需要時請使用上面的 `MasterMemoryDebugRebuild.AutoRebuild`。

## WebGL Notes

- 沒有使用 `Reflection.Emit`、`DynamicMethod`、Thread、原生檔案對話框、`System.Diagnostics.Process`。
- Patch 寫在 `Application.persistentDataPath`（IndexedDB），每次寫入後會呼叫 `FS.syncfs` 同步。
- Export / Copy JSON 會透過 `Plugins/WebGL/MasterMemoryDebugger.jslib` 觸發瀏覽器下載，Import 會開啟瀏覽器的檔案選擇器。
  瀏覽器只允許在使用者點擊後開啟檔案選擇器，若被擋下請再按一次 Import。

## IL2CPP Notes

- Debugger 透過 reflection 讀寫 Record 的 property / backing field，並呼叫產生出來的 `ToImmutableBuilder()` / `Diff()` / `Build()` / `Validate()`。
  這些成員如果只被 reflection 使用，Managed Code Stripping 可能會把它們移除。
- **Development Build 會自動處理**：`MasterMemoryDebuggerLinkerProcessor`（`IUnityLinkerProcessor`）在 build 時產生 link.xml，
  preserve 所有 `[MemoryTable]` Record、`MemoryDatabase` 與 `ImmutableBuilder`。正式版 Build 不受影響。
  （Unity 不會讀取 Package 裡的 link.xml，所以改用 build callback 產生。）
- 需要手動設定時（例如想在所有 Build 保留，或自訂 build pipeline 不會執行 `IUnityLinkerProcessor`），
  可參考範例的 `Samples~/BasicExample/link.xml`，複製到 `Assets/` 底下並改成自己的 assembly / namespace：

  ```xml
  <linker>
    <assembly fullname="MyGame.MasterData">
      <!-- [MemoryTable] records + MasterMemoryGeneratorOptions namespace of the generated code -->
      <namespace fullname="MyGame.MasterData" preserve="all" />
    </assembly>
  </linker>
  ```

- 其他 reflection 只使用 `PropertyInfo` / `FieldInfo` 的 `GetValue` / `SetValue`，以及 `Delegate.DynamicInvoke`（複合主鍵），沒有 `Reflection.Emit`。

## Troubleshooting

| 症狀 | 原因 / 處理 |
| --- | --- |
| 按 F8 沒反應 | 是否在 Play Mode / Development Build？Settings 的 Enabled 與 Toggle Key？Input System 專案請確認 Player Settings 的 Active Input Handling 是 Input System Package 或 Both。 |
| `MasterMemory.dll will not be loaded ... Unable to resolve reference 'MessagePack'` | NuGet 相依套件沒有裝齊。用 Manage NuGet Packages 重新安裝 MasterMemory，或補齊 `packages.config` 後執行 **NuGet > Restore Packages**。 |
| 顯示「No table registered」 | 還沒呼叫 `RegisterDatabase` / `RegisterTable`，或呼叫時 database 尚未載入。 |
| Apply 之後遊戲數值沒變 | 該讀取路徑沒有經過 `TryGetOverride` / `Resolve`，或者是 SecondaryKey / Range 查詢（參考 Query Limitation）。 |
| 欄位顯示 `RO` | 不支援的型別（Dictionary / 巢狀物件 / 元素為複雜型別的 List…）或沒有 setter。 |
| Apply Patch 顯示版本不同 | 用 `SetMasterVersionProvider` 提供正確版本，或在確認後選 Force Apply。 |
| 顯示名稱變成方框 | 預設字型沒有這些字：在 Settings 的 Font 指定含有 CJK 的字型。 |
| 表格欄位太多、看不到 | 用 Columns ▾ 隱藏不需要的欄位、凍結常用欄位，或 Shift + 滾輪水平捲動。 |
| Auto Load 沒有套用 | `SetMasterVersionProvider` 必須在註冊 Table **之前** 呼叫；版本不同時不會自動載入。 |
| UI 被遊戲 UI 蓋住 | 調高 Settings 的 Sorting Order，或指定自己的 PanelSettings。 |
| 下拉選單是白底 | 只有在 Settings 指定了專案自己的 PanelSettings 時才會發生（Debugger 不會去改共用 panel 的樣式）。不指定 PanelSettings 時會使用深色選單。 |
| 編譯錯誤找不到 `MasterMemory` / `PrimaryKeyAttribute` | 請用 NuGetForUnity 安裝 MasterMemory 3.x，並確認 Console 沒有 `MasterMemory.dll will not be loaded` 錯誤。 |

## Tests

Tests 位於 `Tests/Runtime`（Edit Mode + Play Mode）與 `Tests/Editor`。
Repository 的 `Tools/Harness` 可以不開 Unity、用 .NET SDK 編譯並執行大部分的測試（CI 在每個 PR 執行）。
UI 的部分請在發版前照 `Tools/Harness/SMOKE_TEST.md` 在 Unity 中檢查。
在其他專案中執行時，請在 `Packages/manifest.json` 加入：

```json
"testables": [ "com.nesh.mastermemory-debugger" ]
```
