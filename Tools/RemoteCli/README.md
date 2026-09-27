# Remote Debugger CLI

`mmdebug` 是獨立的 .NET 8 命令列工具，直接連接遊戲的 Remote Debugger TCP 服務。它不需要 Unity Editor 或遊戲的紀錄型別 DLL。伺服器只在 Editor / Development Build 且啟動 Remote Server 時提供連線；WebGL 不支援。CLI 與遊戲都需使用 Remote Protocol v5（包含本分支的套件）。

```powershell
dotnet build Tools/RemoteCli/RemoteCli.csproj -c Release
$env:MMDEBUG_CODE = '畫面上顯示的配對碼'
dotnet run --project Tools/RemoteCli -- inspect --host 127.0.0.1
dotnet run --project Tools/RemoteCli -- tables --host 127.0.0.1
dotnet run --project Tools/RemoteCli -- records --host 127.0.0.1 --table SkillMaster --offset 0 --limit 20
dotnet run --project Tools/RemoteCli -- changes --host 127.0.0.1 --table SkillMaster
dotnet run --project Tools/RemoteCli -- validate --host 127.0.0.1
dotnet run --project Tools/RemoteCli -- operations --host 127.0.0.1
```

預設埠是 `7788`；可用 `--port` 指定。 `--timeout` 控制單次 socket 讀寫的秒數，預設 30。 `--code` 可代替 `MMDEBUG_CODE`，但命令列參數可能被本機程序清單記錄。每次命令建立新連線，因此同時只能有一個工具或 CLI 連線。

`records` 的 `value` 是 MessagePack 結構轉出的 JSON：使用陣列編碼的型別會顯示陣列，使用 map 編碼的型別會顯示物件。CLI 不知道主鍵欄位或 C# 型別；`index` 只代表這次快照中的位置，不是穩定 ID。 `sha256` 是原始 MessagePack 位元組的雜湊，供稽核及比較，不代表原始資料版本。

## Patch 匯出、預檢與套用

```powershell
dotnet run --project Tools/RemoteCli -- patch-export --host 127.0.0.1 --output current.patch.json
dotnet run --project Tools/RemoteCli -- patch-plan --host 127.0.0.1 --file edited.patch.json
dotnet run --project Tools/RemoteCli -- patch-apply --host 127.0.0.1 --file edited.patch.json --epoch EPOCH --master-version VERSION --plan-sha SHA --request-id 9a5f2c1e
```

`patch-export` 由遊戲的 `MasterDataPatchService` 產生 Patch JSON。輸出檔必須尚未存在，以免覆蓋先前資料。 `patch-plan` 由遊戲解析 Patch，對所有目標做嚴格預檢，回傳 `serverEpoch`、`masterVersion`、`patchSha`、`planSha` 與每筆 `beforeSha`。把前三個版本值與 `planSha` 保留，用於 `patch-apply`。

`patch-apply` 會重新預檢同一份 Patch；若 JSON、遊戲版本、目標覆寫或原始資料已變，整批拒絕。成功回傳 `appliedRecords`、`appliedFields`、`stateSha` 和 `validation`。一般覆寫可用含一筆紀錄的 Patch；批次編輯可用同一 Patch 內的多筆紀錄。既有紀錄每個變更欄位都要有 `original`，其值須符合遊戲原始 master data；新增紀錄要有 `added: true`，刪除紀錄要有 `deleted: true`。格式範例見 [API_GUIDE.md](API_GUIDE.md)。

如果連線在 `patch-apply` 結果抵達前中斷，**保持相同 Patch 內容、planSha 和 request ID** 重試。遊戲在同一次 Remote Server 運行期間會重播同一結果；同一 ID 搭配不同請求會被拒絕。遊戲重啟後 epoch 改變，須重新預檢，不可盲目重試。若 master version 在同一次伺服器運行期間變更，原 request ID 仍可取得已快取的結果。

## 自訂操作

若遊戲註冊了操作，先執行 `operations` 取得 `id`、`serverEpoch`、`context` 和 `revision`，再呼叫：

```powershell
dotnet run --project Tools/RemoteCli -- invoke --host 127.0.0.1 --id apply-candidate --epoch EPOCH --context CONTEXT --revision 3 --request-id 3f5c1a9e
```

`invoke` 會在新連線的 Welcome 再核對這些值，伺服器也會核對操作的 context/revision。結果不明時使用相同 request ID 重試。

## JSON 契約 v1

標準輸出永遠只有一行 JSON。成功：

```json
{"schemaVersion":1,"ok":true,"data":{"serverEpoch":"...","masterVersion":"...","protocolVersion":5,"tableCount":2,"overrideCount":0},"error":null}
```

失敗：

```json
{"schemaVersion":1,"ok":false,"data":null,"error":{"code":"PATCH_INVALID","message":"No changes applied; patch preflight failed.","details":[{"tableName":"SkillMaster","key":"{\"Id\":1001}","field":"Damage","code":"ORIGINAL_MISMATCH","message":"Master-data original differs from patch original."}]}}
```

`schemaVersion` 是 CLI JSON 契約，和 Remote Protocol `Version=5` 分開。欄位用 camelCase；`records`、`changes` 的結果含 `table`、`total`、`offset` 和陣列。 `validate` 回傳 `{tableName,key,message,isNew}` 陣列。 `operations` 回傳 `{id,label,context,revision,serverEpoch}` 陣列。

| Exit code | Error code | 意義 |
| --- | --- | --- |
| 0 | — | 成功，包括 `NoChange` |
| 2 | `USAGE`, `FILE_ERROR` | 參數或本機檔案錯誤 |
| 3 | `CONNECTION_ERROR` | 無法連線或連線中斷 |
| 4 | `REJECTED`, `VERSION_MISMATCH` | 配對失敗、忙碌或協定版本不同 |
| 5 | `PROTOCOL_ERROR`, `RECORD_DECODE_ERROR` | 無效訊息或紀錄無法轉成 JSON |
| 6 | `STALE`, `CONFLICT` | epoch、master version 或 Patch 計畫已變 |
| 7 | `NOT_FOUND`, `OPERATION_FAILED`, `PATCH_INVALID`, `PATCH_FAILED` | 遠端拒絕 |
| 8 | `TIMEOUT` | 連線或讀取逾時 |
| 9 | `INTERNAL_ERROR` | 非預期的 CLI 錯誤 |

CLI 會接收遊戲送來的完整 Welcome，再於本機分頁；`--limit` 不能減少網路傳輸量。不要把配對碼或包含敏感資料的 JSON 輸出寫入公開日誌。
