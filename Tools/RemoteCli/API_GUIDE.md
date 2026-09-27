# Remote Debugger API 與自動化指南

## 現有能力與可用入口

| 能力 | Unity 端 C# API | Remote Protocol v5 | CLI v1 |
| --- | --- | --- | --- |
| 資料表與紀錄 | `MasterMemoryDebugRegistry`、`MasterMemoryTableDescriptor` | Welcome 全量快照 | `tables`、`records` |
| 資料查詢 | `MasterRecordQuery` | 無查詢訊息 | 讀取 `records` 後由呼叫端查詢 |
| Override 設定/移除/刪除 | `MasterMemoryDebugRuntime`、`MasterDataOverrideStore` | 既有 `Changes` 同步；新 Patch 請求有確認 | 單筆 Patch 預檢/套用 |
| Batch Edit | `MasterMemoryBatchEdit.Apply` | Patch 可含多筆變更，一次提交 | 多筆 Patch 預檢/套用 |
| Changes 檢視 | `MasterMemoryChangeSummary`、`MasterMemoryChangeLog` | Welcome 的覆寫與後續 `Changes` | `changes` 快照 |
| Patch 匯出/套用 | `MasterDataPatchService`、`MasterDataPatchSerializer` | Export、Plan、Apply 請求/回覆 | `patch-export`、`patch-plan`、`patch-apply` |
| Validation | `MasterMemoryDebugValidation.Run` | `ValidateRequest` / `ValidateResult`；Patch 套用結果 | `validate`；Patch 結果 |
| 遊戲自訂操作 | `MasterMemoryDebugRemote.RegisterOperation` | `Operations` / `OperationRequest` / `OperationResult` | `operations`、`invoke` |

遊戲主執行緒在 `MasterMemoryDebugRemote.Pump()` 處理收到的 Patch 請求。Remote Server 在連線期間只允許一個客戶端。紀錄使用遊戲設定的 MessagePack resolver；Patch 由遊戲端的既有 serializer 解析，再由嚴格預檢器建立具型別的目標變更。CLI 不猜測 MessagePack 物件結構。

## Patch 格式與安全流程

既有 Patch JSON 格式（此例修改一筆紀錄，第二筆表示刪除；多筆欄位和紀錄就是批次編輯）：

```json
{
  "formatVersion": 2,
  "masterVersion": "2026.09.26.001",
  "exportedAt": "2026-09-27T10:00:00Z",
  "tables": [{
    "tableName": "SkillMaster",
    "memoryTableName": "skill",
    "recordType": "Game.SkillMaster",
    "records": [{
      "primaryKey": {"Id": 1001},
      "changes": [{"field": "Damage", "original": 120, "value": 185}]
    }, {
      "primaryKey": {"Id": 1003},
      "deleted": true,
      "changes": []
    }]
  }]
}
```

1. 用 `inspect` 確認遊戲與 master version；讀取 `tables`、`records`、`changes`。
2. 用 `patch-export` 匯出現有覆寫作備份；準備新 Patch，既有紀錄的每個 `original` 值須符合原始 master data。Patch 中所有目標不得重複。
3. 執行 `patch-plan --file ...`。遊戲核對格式、master version、表、主鍵、可編輯欄位和值；任何錯誤都不修改資料。結果的每筆 `beforeSha` 表示目標當前的覆寫狀態，`planSha` 綁定 Patch SHA、epoch、master version 與所有目標雜湊。
4. 將 `serverEpoch`、`masterVersion`、`planSha` 傳入 `patch-apply`，並提供唯一且可保存的 request ID。遊戲重新規劃並核對雜湊，在同一個 store lock 下確認所有目標仍相同，再一次提交所有覆寫；失敗時不會提交部分目標。
5. 檢查 `ok`、`appliedRecords`、`appliedFields`、`stateSha`、`errors`、`validation`。 `stateSha` 是這批目標提交後的彙總雜湊，並非整個資料庫版本。驗證錯誤會列在結果中；若遊戲未設定驗證，陣列可能為空。
6. 逾時或連線中斷後，以相同 Patch、planSha 與 request ID 重試。伺服器在同一次運行期間快取結果並重播；同一 ID 的不同請求回傳 `CONFLICT`。遊戲重啟需重新預檢。

Protocol v5 保留既有桌面 Remote Editor 的 `Changes` 訊息；該同步本身仍沒有逐筆 ACK。要求可核對成功與整批拒絕的自動化應使用 Patch Plan/Apply。

## Unity 端 C# 範例

```csharp
MasterMemoryDebugRemote.StartServer(7788);
MasterMemoryDebugRuntime.SetOverride<SkillMaster, int>(1001, editedSkill);
bool changed = MasterMemoryDebugRuntime.RemoveOverride<SkillMaster, int>(1001);
string json = MasterDataPatchService.CreatePatchJson();
MasterDataPatchExporter.Export(json, "master-patch.json");
```

`MasterDataPatchService.Apply(patch, force: false, replaceExisting: true)` 是原有互動式套用器：會檢查 master version，對部分不符的欄位提出警告並跳過。遠端自動化使用 Protocol v5 的嚴格預檢/提交路徑。遊戲特定操作可用 `MasterMemoryDebugRemote.RegisterOperation` 註冊 callback，並在 context/revision 變更時呼叫 `NotifyOperationsChanged()`。
