# Remote Editor Build 操作教學

Remote Editor 是從**自己的 Unity 遊戲專案**建置的桌面工具。它連到正在執行的遊戲，讓測試、企劃與開發人員在電腦上查看並調整 MasterMemory 主資料。工具與遊戲使用同一份 Record 型別；遊戲必須是 Unity Editor 或 Development Build，並啟動遠端伺服器。正式版 Build 不提供 Debugger 功能。

> Remote Editor 修改的是執行時的 Override，不會寫回原始 `MemoryDatabase`、Excel 或其他主資料來源。要保存調整結果，請將變更存成 Patch 或匯出 TSV，再由專案流程回填。

## 可以做什麼

| 功能 | 在工具中的用法 |
| --- | --- |
| 查看主資料 | 在 **Data** 選 Table，瀏覽 Record、排序、釘選 Table；可隱藏、凍結與調整欄寬。 |
| 搜尋 | 用欄位條件縮小目前 Table 的資料；**Find** 可搜尋所有已註冊 Table，包括 List 與巢狀物件。 |
| 調整數值 | 在 Inspector 修改可編輯欄位，或以 **Batch Edit…** 對搜尋結果批次 Set / Add / Multiply。可新增、複製、刪除 Record，並 Undo / Redo。 |
| 檢查變更 | **Changes** 顯示已改動的欄位，可複製 TSV，也可貼回試算表修改後的 TSV。 |
| 保存與分享 | **Patches** 可儲存、套用、合併、比較及匯出 JSON Patch；Patch 儲存在**工具所在電腦**。 |
| 查看驗證問題 | **Validation** 顯示遊戲端 MasterMemory `Validate()` 的結果；需遊戲端使用 `AutoRebuild` 且開啟驗證。 |
| 執行專案操作 | 若遊戲有透過 `RegisterOperation` 登記操作，**Remote** 對話框會出現按鈕與回傳結果。 |

連線後，工具看到的是遊戲目前的 Table 與 Override。工具送出的修改會傳到遊戲；遊戲端修改也會同步回工具。遊戲是否在 Gameplay 查詢中讀到這些值，取決於專案是否使用 `TryGetOverride` / `Resolve` 或 `MasterMemoryDebugRebuild.AutoRebuild`（見[套件 README 的整合方式](README.md#integrate-override)）。

## 開始前確認

1. 遊戲專案已安裝 MasterMemory 3.x、NuGetForUnity 與本 Package，並已註冊要查看的 Table。基本安裝與註冊方式見[套件 README](README.md#quick-start)。
2. 遊戲端有接入 Override 讀取路徑。只有註冊 Table 而沒有接入 Gameplay 讀取時，工具可以顯示修改，但遊戲邏輯可能仍讀到原始值。
3. 用**同一版本的遊戲專案**建置工具與遊戲。Record 型別或序列化設定改變後，請重建工具；遠端協定版本不同的工具與遊戲無法連線。
4. 目標遊戲是 Editor Play Mode 或 Development Build。桌面工具可從 Windows、macOS 或 Linux 的 Unity Editor 建置給該桌面平台；遊戲端不可使用 WebGL 遠端連線。
5. 確認工具可到達遊戲的網路。預設 TCP `7788` 用於連線；區域網路搜尋使用 UDP `7787`。需要配對碼；一次只允許一個工具連線。

## 第一次試用：範例場景

這條路徑可先在同一台電腦驗證工具，再接入自己的遊戲。

1. 在 Package Manager 匯入 **MasterMemory Runtime Debugger > Samples > Basic Example**。
2. 選 **Tools > MasterMemory Debugger > Remote Editing > Create Example Game Scene**。Unity 會建立 `Assets/MasterMemoryDebugger/MasterMemoryExampleGame.unity`，並放入已勾選 **Start Remote Server** 的 `ExampleDebuggerLauncher`。若未匯入範例，這個選單會提示先匯入。
3. 開啟範例場景並進入 Play Mode。從 Console 讀取六位數配對碼；也可按 **F8 > Remote** 查看 IP、Port 與配對碼。
4. 選 **Tools > MasterMemory Debugger > Remote Editing > Build Remote Editor Tool…**，指定輸出資料夾。選單會在缺少工具場景時自動建立它。
5. 執行產出的工具。在連線對話框輸入 `127.0.0.1`、`7788` 與第 3 步的配對碼，按 **Connect**。
6. 連線成功後，選擇一張 Table，修改一個非 Key 欄位並按 Inspector 的 **Apply**。遊戲 Console 與工具的 **Changes** 可用來確認變更；再用 **Reset** 還原。

同一台電腦也能在 Editor 裡開啟 **Create Remote Editor Tool Scene** 產生的場景並按 Play，直接把 Editor 當工具；若遊戲也在 Editor Play Mode，請用另一個 Unity 執行個體或已建置的遊戲。工具場景預設存於 `Assets/MasterMemoryDebugger/MasterMemoryRemoteEditor.unity`。

## 正式接入自己的遊戲

### 1. 設定遊戲端

在 **Project Settings > MasterMemory Debugger**：

- 開啟 **Remote Server**，讓 Development Build 啟動後自動等待連線；或在程式中呼叫 `MasterMemoryDebugRemote.StartServer()`。有 Debugger UI 時，也可按 **F8 > Remote > Start** 手動啟動。
- **Remote Port** 預設為 `7788`。若需要固定配對碼，設定 **Remote Pairing Code**；留空會在每次啟動時產生新的六位數配對碼，可從遊戲端 Remote 對話框或 Console 取得。
- 若測試機只需被桌面工具連線，可關閉 **Include Debugger UI** 以排除 UI 與字型。此時請設定固定配對碼，因為遊戲畫面不會顯示隨機碼；Table 註冊與 Override 整合仍須保留。

使用 Unity 的 Build Profiles / Build Settings 建置遊戲時，勾選 **Development Build**。Windows、macOS、Android、iOS 等支援 socket 的平台可作為遊戲端；正式版 Build 或設定了 `MMDEBUGGER_DISABLE` 的 Build 無法使用遠端編輯。

### 2. 建置桌面工具

從**同一個遊戲專案**選 **Tools > MasterMemory Debugger > Remote Editing > Build Remote Editor Tool…**，選擇輸出資料夾。工具場景只有 `MasterMemoryRemoteEditor` 元件，遊戲本身的場景與邏輯不會在工具中執行，但 Record 型別仍需一起編譯。

選單會為目前 Unity Editor 所在的桌面平台建置 Development Build：Windows 輸出 `MasterMemoryRemoteEditor.exe`，macOS 輸出 `.app`，Linux 輸出執行檔。請保留 Unity 產生的整個輸出資料夾，分發時不要只複製 Windows 的 `.exe`。建置期間工具會暫時使用 Mono、1600 × 900 可調整視窗與獨立 Product Name；建置後會還原專案的 Player Settings。工具的 Patch 與 PlayerPrefs 因此與遊戲分開。

也可以自行建立空場景，加入 **MasterMemory Debugger > Remote Editor** 元件，並只建置這個場景；仍要勾選 **Development Build**。通常使用選單即可。

### 3. 連線

1. 啟動遊戲並確認遠端伺服器正在等待連線；記下遊戲 IP、Port 和配對碼。
2. 啟動桌面工具。Debugger 會自動開啟並佔滿視窗；按 **Remote** 開啟或返回連線對話框。
3. 同一個區域網路可在 **Games on the network** 按 **Search**，點選遊戲以填入位址與 Port；再輸入配對碼並按 **Connect**。只有一台遊戲被找到時，位址與 Port 會自動填入。
4. 若搜尋不到，直接輸入遊戲 IP 與 Port。訪客 Wi-Fi、VPN 或防火牆可能阻擋 UDP 搜尋，但手動 TCP 連線仍可使用。
5. 確認工具顯示已連線、Table 清單出現。要結束連線按 **Disconnect**；遊戲端可按 **Remote > Stop**。

Windows 遊戲第一次開放 Port 時，允許私人網路的防火牆提示。iOS 遊戲需要在 Info.plist 說明區域網路使用用途，並允許系統提示。Android 透過 USB 時，可在電腦執行 `adb forward tcp:7788 tcp:7788`，然後讓工具連 `127.0.0.1:7788`；若改了 Port，兩個 `7788` 都要改。

## 連線後的建議操作順序

1. 在 **Data** 選 Table，用搜尋條件找出目標 Record；點選一筆後，先看 Inspector 的 **Original** 與目前值。
2. 修改欄位後按 **Apply**。Key 欄位唯讀；新增、複製、刪除 Record 會留在 Override Layer。可用 **Undo / Redo** 或 Inspector 的 **Revert / Reset** 撤回。
3. 在 **Changes** 確認所有修改。需要大量編輯時可 **Copy TSV** 到試算表，修改後用 **Paste TSV…** 預覽並套用。
4. 有 `AutoRebuild` 時，檢查 **Validation** 是否出現新失敗；對遊戲的查詢與畫面也做實際驗證。
5. 在 **Patches** 用 **Save As…** 保存當前調整，或 **Export** JSON 交給其他人。之後可 **Apply**、**Merge** 或 **Compare…**；Patch 不會自動回填主資料來源。

工具斷線後會保留最後收到的資料，並每 3 秒嘗試重連同一個位址。配對碼錯誤或已有其他工具連線時不會自動重試；遊戲重啟後若配對碼會變，請重新取得配對碼並連線。重連成功後，以遊戲目前的資料為準。工具視窗沒有 Close；F8 / Esc 不會關閉工具的 Debugger。

## 常見問題

| 現象 | 檢查方式 |
| --- | --- |
| 沒有找到遊戲 | 確認遊戲已啟動 Remote Server；檢查 UDP `7787`、網段與 VPN。改用手動 IP、TCP Port 連線。 |
| 連不上或配對失敗 | 核對 IP、Port、配對碼與防火牆；確認遊戲仍在 Editor / Development Build，且沒有另一個工具佔用連線。 |
| 工具找不到 Table | 確認遊戲已註冊 Table，並查看遊戲 Console 的註冊或 MessagePack 錯誤；工具與遊戲應由相同版本專案建置。 |
| 工具改值後遊戲沒有反應 | 確認 Gameplay 經過 `TryGetOverride` / `Resolve`；SecondaryKey、Range 或 `All` 查詢需考慮 `AutoRebuild`。已有的舊 Record 參考不會自動更新。 |
| IL2CPP 連線後無法傳送 Record | 將載入 `MemoryDatabase` 時使用的 MessagePack options（含專案與 MasterMemory resolver）設定到遊戲端的 `MasterMemoryDebugRemote.SerializerOptions`；工具端也使用相同設定。查看遊戲 Console 的序列化檢查結果。 |
| Validation 沒有結果 | 遊戲端需使用 `MasterMemoryDebugRebuild.AutoRebuild` 並啟用驗證；驗證大型 Table 可能很慢，可在 Validation 頁籤手動執行。 |
| Patch 在遊戲裝置上找不到 | 工具的 Patch 保存在桌面工具的 `Application.persistentDataPath/MasterMemoryDebugger/`，不是遊戲裝置的資料夾；可從工具匯出 JSON。 |

## 使用界線

- 遠端編輯只供開發與測試。TCP 傳輸沒有加密，請在可信任的開發網路使用；配對碼不是加密機制。
- 原始 `MemoryDatabase` 不會被修改。既有 Record 的 PrimaryKey / SecondaryKey 不可編輯；Schema 變更也不受支援。新增與刪除的 Gameplay 行為取決於 Override 整合方式，詳見[套件 README](README.md#integrate-override)。
- WebGL 不支援 socket 遠端連線。手機、IL2CPP 與跨平台連線請按專案的[smoke test 清單](../../Tools/Harness/SMOKE_TEST.md)實機驗證。
- 如要在連線工具上提供遊戲特定動作，可由遊戲程式使用 `MasterMemoryDebugRemote.RegisterOperation` 登記；詳細範例與 Context / Revision 規則見[Remote Editing API 說明](README.md#remote-editing遠端編輯實機)。
