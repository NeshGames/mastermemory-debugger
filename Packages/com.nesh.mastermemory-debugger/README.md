# MasterMemory Runtime Debugger

`com.nesh.mastermemory-debugger`

[Cysharp/MasterMemory](https://github.com/Cysharp/MasterMemory) v3 的 Runtime 資料查閱與開發用 Override 工具，UI 完全使用 **UI Toolkit**。

- 在 Unity Editor / Development Build 中瀏覽、搜尋所有 MasterMemory Table
- 對既有 Record 的非 Key 欄位建立 **Runtime Override**（MasterMemory 本體維持 Immutable）
- 將修改過的欄位存成 / 匯出為 JSON Patch，重開遊戲後可載入，也能交給企劃回填主資料

> 本 Package 的定位是 **Development Runtime Inspector + Value Override Tool**，不是 Runtime Database Editor。
> 不支援新增 / 刪除 / 複製 Record、修改 PrimaryKey / SecondaryKey、Schema 變更、重建或替換 MemoryDatabase。

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
   https://github.com/NeshGames/mastermemory-debugger.git?path=/Packages/com.nesh.mastermemory-debugger
   ```

   指定版本：在 URL 最後加上 `#v0.1.0` 之類的 tag。

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

### （選用）用 ImmutableBuilder 重建 Gameplay Database

如果希望 SecondaryKey / Range / `All` 查詢也能讀到 Override，可以由**專案端**訂閱 `OverridesChanged`，再用 MasterMemory 官方的 `ImmutableBuilder` 重建：

```csharp
MasterMemoryDebugRuntime.OverridesChanged += () =>
{
    var builder = originalDatabase.ToImmutableBuilder();   // 一定要從原始 database 開始
    builder.Diff(MasterMemoryDebugRuntime.GetOverrides<SkillMaster>());
    builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ItemMaster>());
    service.Database = builder.Build();
};
```

代價：每次變更都會重建並重新排序整個 database；其他地方持有的舊 database / record 參考不會更新；每張表都要手寫一行 `Diff`。
Package 本身永遠不會重建或替換 database。完整範例見 `Samples~/BasicExample/ExampleDatabaseRebuilder.cs`。

## UI Toolkit Runtime Debugger

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ MasterMemory Runtime Debugger  Master: v1  2 overrides   [Changes (2)][A-][A+][Close] │
├──────────────┬───────────────────────────────────────────────────────────────┤
│ Tables       │ [Damage>100 Element=Fire.................] [ ] Modified Only   │
│ ▼ Battle (5) │ Primary Key │ Name      │ Mod │ Category (SK) │ Damage ▼│ ...   │
│   SkillM. *2 │ 1004        │ Thunder   │     │ 1             │ 180     │       │
│   ItemMaster │ 1001        │ Fireball  │  *  │ 1             │ 185     │       │
│ ▶ Economy (1)│ 2 / 2005 records                                              │
├──────────────┴───────────────────────────────────────────────────────────────┤
│ SkillMaster 1001 [Overridden]  [Copy JSON][Apply Override][Revert Edits][Reset Record] │
│ Id        PK   1001                                                          │
│ Damage         [185            ]                         Original: 120       │
│ EffectIds RO   ▶ [2] 10, 11                                                  │
├──────────────────────────────────────────────────────────────────────────────┤
│ Patch [balance-A ▼][balance-A] [Save][Load][Delete][Import][Export][Open Folder] [Reset All] │
│ status...                                                             [Log (12)] │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Record 表格

- Record 列表是可排序的多欄表格（`MultiColumnListView`，virtualization）：Primary Key、Name、Mod，以及每個欄位一欄（SecondaryKey 標 `(SK)`，複雜型別顯示預覽）。
- 點欄位標題排序（再點一次反向）；排序會套用在所有符合條件的資料上，再取前 `Max Search Results` 筆（預設 500）。
- 有 Override 的格子若和原始值不同，會以橘色顯示。

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

### Inspector

- 列出所有 public property / field：
  - `PK` / `SK`：永遠唯讀
  - 支援編輯：`int uint short ushort long ulong byte sbyte float double bool string enum`、`[Flags] enum`（以文字輸入）、`Vector2 Vector3 Vector2Int Vector3Int Color`、以及上述型別的 `Nullable<T>`
  - Array / List / Dictionary / 巢狀物件：唯讀的可折疊樹狀檢視（最多 3 層、每層最多 100 項，展開時才建立）
  - 有修改的欄位會顯示 `Original: xxx`
- **Apply Override** 會把編輯中的副本存進 Override Store；如果所有值都和原始值相同，會改為移除 Override。
- **Copy JSON**：把整筆 Record（包含陣列與巢狀物件、未套用的編輯）複製為 JSON。Editor / Windows 複製到剪貼簿，WebGL 下載成檔案。
- 有未套用的編輯時切換 Record / Table，會詢問 **Apply / Discard / Cancel**。

### Changes（修改總覽）

標題列的 **Changes (N)** 會把主畫面切換成所有 Override 的總覽：每筆 Record 的修改欄位（原始值 → 目前值），可以 **Open**（跳到該筆 Record）或 **Reset**。
原始 Record 已不存在（例如主資料刪掉了）或 Table 沒有註冊的 Override 會以紅色標示。程式中可用 `MasterMemoryChangeSummary.Build()` 取得同樣的資料。

### Log 與 Console

- 狀態列右側的 **Log (N)** 會展開最近 50 則訊息（狀態、Patch 警告、修改內容），在沒有 Console 的實機上也看得到。
- Apply Override / Reset Record / Reset All / Load Patch 都會在 Console 印出修改內容（可在 Settings 關閉）：

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
| Enter | Inspector 中：Apply Override；對話框中：執行主要按鈕（刪除 / 覆蓋 / Reset All 等危險操作不會被 Enter 觸發） |
| Esc | 關閉對話框，沒有對話框時關閉 Debugger |

- **Reset All** 會先跳出確認視窗：`Reset all MasterMemory runtime overrides?`
- `A-` / `A+` 可調整 UI 縮放（只在使用 Debugger 自己建立的 PanelSettings 時顯示）。

Runtime 的 UI Toolkit 沒有 `ColorField`、`ToolbarSearchField`、`EnumFlagsField`，所以 Color 使用 RGBA 四個 `FloatField`，搜尋框使用 `TextField`，Flags enum 使用文字輸入。

## Open / Close / Toggle

```csharp
RuntimeMasterMemoryDebugger.Open();
RuntimeMasterMemoryDebugger.Close();
RuntimeMasterMemoryDebugger.Toggle();
bool isOpen = RuntimeMasterMemoryDebugger.IsOpen;
```

- 預設熱鍵 **F8**（可在 Project Settings 修改），支援 Input System 與舊的 Input Manager。
- 行動裝置：沒有內建手勢，請從專案既有的 Debug Menu 呼叫 `Toggle()`。
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

| 按鈕 | 行為 |
| --- | --- |
Patch 可以命名，存成 `Application.persistentDataPath/MasterMemoryDebugger/<名稱>.patch.json`。
底部工具列：`Patch [已儲存的 Patch ▼] [名稱] [Save Patch] [Load Patch] [Delete] [Import] [Export] [Open Folder] ... [Reset All]`

| 控制項 | 行為 |
| --- | --- |
| 下拉選單 | 列出已儲存的 Patch；選擇後名稱欄位會帶入同樣的名稱 |
| 名稱欄位 | Save Patch 使用的名稱（例如 `balance-A`）。不能用在檔名的字元會換成 `_` |
| Save Patch | 以名稱欄位儲存；名稱已存在且不是目前選取的 Patch 時會先確認是否覆蓋 |
| Load Patch | 載入下拉選單選取的 Patch，**取代**目前所有 Override |
| Delete | 刪除下拉選單選取的 Patch（目前的 Override 不受影響） |
| Import | 把 Patch 檔加入清單（之後用 Load Patch 套用）。Editor：檔案對話框；**WebGL：瀏覽器上傳**；其他平台：貼上 JSON。同名時會確認是否覆蓋 |
| Export | 匯出目前的 Override（檔名 `<名稱>-<時間>.json`）。Editor：存檔對話框；**WebGL：瀏覽器下載**；其他平台：`.../MasterMemoryDebugger/exports/` |
| Open Folder | 開啟資料夾（Editor / Windows / macOS / Linux） |

程式碼中也可以直接使用：`MasterDataPatchStorage.Save(patch, "balance-A")`、`Load("balance-A")`、`ListPatchNames()`、`Delete("balance-A")`。

載入時：

- Master Version 不同 → 顯示警告，可選 **Cancel** 或 **Force Load**。
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
| Allow Patch Save | true | 顯示 Save Patch / Export |
| Auto Load Patch | false | 註冊 Table 時自動載入已儲存的 Patch |
| Toggle Key | F8 | |
| Max Search Results | 500 | |
| Show Secondary Keys | true | 在 Inspector 顯示 SecondaryKey 欄位（永遠唯讀） |
| Log Level | Warning | |
| Log Override Changes | true | Apply / Reset / Reset All / Load Patch 時在 Console 列出改了哪些欄位與前後值（Editor 中以顏色標示） |
| Default Patch Name | debug | 預設選取的 Patch 名稱，也是 Auto Load Patch 載入的 Patch |
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

需要時請參考上面的「用 ImmutableBuilder 重建 Gameplay Database」。

## WebGL Notes

- 沒有使用 `Reflection.Emit`、`DynamicMethod`、Thread、原生檔案對話框、`System.Diagnostics.Process`。
- Patch 寫在 `Application.persistentDataPath`（IndexedDB），每次寫入後會呼叫 `FS.syncfs` 同步。
- Export / Copy JSON 會透過 `Plugins/WebGL/MasterMemoryDebugger.jslib` 觸發瀏覽器下載，Import 會開啟瀏覽器的檔案選擇器。
  瀏覽器只允許在使用者點擊後開啟檔案選擇器，若被擋下請再按一次 Import。

## IL2CPP Notes

- `RegisterTable<SkillMaster, int>()` / `RegisterDatabase()` 會建立實際的型別參考，一般不會被 strip。
- 反射只使用 `PropertyInfo` / `FieldInfo` 的 `GetValue` / `SetValue`，以及 `Delegate.DynamicInvoke`（複合主鍵）。
- 只有在實際發生 stripping 時，才需要在專案加入 `link.xml`，例如：

  ```xml
  <linker>
    <assembly fullname="MyGame.MasterData">
      <type fullname="MyGame.SkillMaster" preserve="all" />
    </assembly>
  </linker>
  ```

  不需要 preserve 整個 assembly。

## Troubleshooting

| 症狀 | 原因 / 處理 |
| --- | --- |
| 按 F8 沒反應 | 是否在 Play Mode / Development Build？Settings 的 Enabled 與 Toggle Key？Input System 專案請確認 Player Settings 的 Active Input Handling 是 Input System Package 或 Both。 |
| `MasterMemory.dll will not be loaded ... Unable to resolve reference 'MessagePack'` | NuGet 相依套件沒有裝齊。用 Manage NuGet Packages 重新安裝 MasterMemory，或補齊 `packages.config` 後執行 **NuGet > Restore Packages**。 |
| 顯示「No table registered」 | 還沒呼叫 `RegisterDatabase` / `RegisterTable`，或呼叫時 database 尚未載入。 |
| Apply 之後遊戲數值沒變 | 該讀取路徑沒有經過 `TryGetOverride` / `Resolve`，或者是 SecondaryKey / Range 查詢（參考 Query Limitation）。 |
| 欄位顯示 `RO` | 不支援的型別（Array / List / 巢狀物件…）或沒有 setter。 |
| Load Patch 顯示版本不同 | 用 `SetMasterVersionProvider` 提供正確版本，或在確認後選 Force Load。 |
| Auto Load 沒有套用 | `SetMasterVersionProvider` 必須在註冊 Table **之前** 呼叫；版本不同時不會自動載入。 |
| UI 被遊戲 UI 蓋住 | 調高 Settings 的 Sorting Order，或指定自己的 PanelSettings。 |
| 下拉選單是白底 | 只有在 Settings 指定了專案自己的 PanelSettings 時才會發生（Debugger 不會去改共用 panel 的樣式）。不指定 PanelSettings 時會使用深色選單。 |
| 編譯錯誤找不到 `MasterMemory` / `PrimaryKeyAttribute` | 請用 NuGetForUnity 安裝 MasterMemory 3.x，並確認 Console 沒有 `MasterMemory.dll will not be loaded` 錯誤。 |

## Tests

Tests 位於 `Tests/Runtime`（Edit Mode + Play Mode）與 `Tests/Editor`。
在其他專案中執行時，請在 `Packages/manifest.json` 加入：

```json
"testables": [ "com.nesh.mastermemory-debugger" ]
```
