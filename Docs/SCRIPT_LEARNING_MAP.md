# Duck 專案腳本學習地圖

更新日期：2026-10-01  
適用範圍：專案自行維護的 `Assets/Scripts` 與 `Assets/Editor`。

## 先看結論

這個專案**非常足夠**作為下一階段的學習材料，而且目前更需要的是「讀懂、驗證、重構已存在的系統」，不是再加入一個大功能。

目前可學的自有程式碼約為：

| 範圍 | 檔案數 | 約略行數 | 用途 |
| --- | ---: | ---: | --- |
| `Assets/Scripts` | 30 | 10,314 | 實際遊戲、UI、連線、AI 與特效 |
| `Assets/Editor` | 10 | 3,907 | 用選單自動建置場景、Prefab 與設定 |

`Assets/Photon`、`Assets/TextMesh Pro`、`Assets/TutorialInfo` 是套件或範例程式碼，不是現在要逐檔學習的對象。專案目前沒有自動化測試；這是完成前幾個單元後最值得補的一塊。

## 腳本目錄

`Assets/Scripts` 現在依責任分類；移動時保留每個檔案的 `.meta`，因此場景與 Prefab 的 GUID 引用不受影響。

| 目錄 | 職責 |
| --- | --- |
| `AI` | CPU、敵人行為與路徑規劃 |
| `Audio` | 音訊通道元件 |
| `Camera` | 遊戲鏡頭 |
| `Characters` | 鴨子的移動、外觀、名牌與互動 |
| `Combat` | 手杖、攻擊與拾取規則 |
| `Core` | 共用遊戲設定與畫質設定 |
| `Level` | 關卡流程、陣營與目標物 |
| `Networking` | Fusion 房間、同步玩家與場景轉移 |
| `UI` | 選單、選角與局內提示 |
| `Utilities` | 開發／展示用小工具 |
| `VFX` | 程序化魔法與視覺特效 |

## 閱讀原則

不要依檔名字母順序讀，也不要一開始碰最大的檔案。每一單元採用同一個循環：

1. 在 Unity 找到這個腳本掛在哪個 GameObject／Prefab。
2. 先說出它的「輸入、狀態、輸出」各是什麼。
3. 只改一個可見數值，進 Play Mode 驗證猜想。
4. 記錄一個問題或一個想重構的地方，再進下一檔。

建議每次只讀一個小功能；讀完要能關掉文件，自己畫出資料怎麼流動。

## 學習順序

### Unit 0：Unity 與 C# 小元件（先建立手感）

| 優先 | 腳本 | 學什麼 | 小練習 |
| --- | --- | --- | --- |
| 1 | `PreviewTurntable.cs` | `MonoBehaviour`、`Update`、Transform 旋轉 | 加一個可調整速度的 Inspector 欄位。 |
| 2 | `AudioChannelVolume.cs` | enum、`AudioMixer`、設定值 | 增加一個「靜音」按鈕。 |
| 3 | `GameSettings.cs` | 靜態設定、`PlayerPrefs` | 將一項設定在重開遊戲後仍保留。 |
| 4 | `GraphicsQualityController.cs` | Unity `QualitySettings`、UI 事件 | 新增一個畫質預設並顯示目前名稱。 |
| 5 | `DuckSinglePlayerAppearance.cs` | `MaterialPropertyBlock`、角色外觀 | 增加一個新外觀色。 |
| 6 | `DuckPlayerNameplate.cs` | 世界空間文字、`LateUpdate` | 加上距離越遠字越小或淡出的效果。 |
| 7 | `FusionColorPad.cs` | Trigger、Component 查找 | 做一個進入後恢復原色的地板。 |

完成標準：你能解釋 `Awake`、`Start`、`Update`、`LateUpdate` 的用途差異，並能自行加一個序列化欄位與按鈕事件。

### Unit 1：角色、碰撞與鏡頭（本機遊戲基本功）

| 優先 | 腳本 | 學什麼 | 注意點 |
| --- | --- | --- | --- |
| 8 | `DuckKinematicMotor.cs` | 靜態工具類別、`Rigidbody.SweepTest`、安全位移 | 這是共用的碰撞移動層。 |
| 9 | `DuckMover.cs` | 輸入、跳躍、重力、動畫、拾取範圍 | 不要一次讀完；先追 `Move`、再追跳躍、最後追拾取。 |
| 10 | `RPGCameraFollow.cs` | 跟隨鏡頭、輸入、遮擋物淡出 | 檔案較長，但尚未涉及網路。 |

搭配閱讀：[UNIT1_Fusion_Movement_Incident.md](UNIT1_Fusion_Movement_Incident.md)。它記錄了為什麼 Fusion 網路物件不能只依賴 Rigidbody 速度，以及目前為何改為 kinematic motor。

完成標準：能指出「最後真正寫入角色位置」的是哪一行，並能安全調整走速、跳躍高度與鏡頭距離。

### Unit 2：單機戰鬥與視覺回饋（看得到、聽得懂）

| 優先 | 腳本 | 學什麼 |
| --- | --- | --- |
| 11 | `Unit1WandPickup.cs` | enum、能力資料、撿取／掉落、持有物視覺 |
| 12 | `DuckWandAttack.cs` | Coroutine、攻擊前搖、範圍判定、傷害分派 |
| 13 | `Unit1WandVisualEffects.cs` | 程式建立特效、LineRenderer、生命週期 |
| 14 | `VFX/RuneCircleCaster.cs` | 施法入口與特效呼叫 |
| 15 | `VFX/RuneCircleVfx.cs` | 較完整的程序化特效；拆成小段讀 |
| 16 | `FirstLevelEnemySpawner.cs` | Prefab 實例化、出生點、目標 HUD |

完成標準：你可以新增一個很小的能力變體，例如只改顏色、冷卻與擊退，不破壞既有三種手杖。

### Unit 3：選角、選單與場景流程（玩家從哪裡進來）

| 優先 | 腳本 | 學什麼 |
| --- | --- | --- |
| 17 | `CharacterSelectionController.cs` | UI 分頁、角色選擇、資料傳遞 |
| 18 | `MainMenuController.cs` | 主選單 UI 組裝、場景切換、按鈕綁定 |
| 19 | `FactionRouletteHud.cs` | Coroutine 驅動的 UI 動畫 |
| 20 | `FactionCrystalAlertHud.cs` | 事件通知型 HUD |

完成標準：可以從「主選單 → 選角 → 進場」畫出流程圖，並加上一個不會破壞存檔的 UI 設定。

### Unit 4：Fusion 多人連線（先理解權限，再改功能）

| 優先 | 腳本 | 學什麼 | 先問自己的問題 |
| --- | --- | --- | --- |
| 21 | `FusionSessionLauncher.cs` | 建立／加入房間、`NetworkRunner` | Runner 何時建立？ |
| 22 | `FusionLobbyReadyController.cs` | 大廳 Ready 狀態 | 哪一方有權開局？ |
| 23 | `FusionPlayerSceneTransfer.cs` | 場景轉移、玩家與鏡頭重新掛接 | 物件跨場景後誰負責放置？ |
| 24 | `FusionDuckPlayer.cs` | `NetworkBehaviour`、Networked 狀態、RPC、Authority | 狀態由誰寫、誰只負責顯示？ |

本單元禁止「看不懂就移除 Authority 判斷」。每次修改都要用兩個遊戲視窗驗證，至少觀察本機與遠端各一次。

完成標準：你能分清楚 `InputAuthority`、`StateAuthority`、`FixedUpdateNetwork` 與 `Render` 的責任，並能解釋一個狀態為何要同步。

### Unit 5：規則、陣營與關卡控制（系統如何一起工作）

| 優先 | 腳本 | 學什麼 |
| --- | --- | --- |
| 25 | `FactionTeam.cs` | 集中管理陣營名稱、顏色、敵我判斷 |
| 26 | `FactionCrystal.cs` | 網路化目標物、血量、防護、視覺狀態 |
| 27 | `Unit1GameDirector.cs` | 關卡開局、CPU 補位、分隊、生成水晶與目標 HUD |

搭配閱讀：[UNIT1_First_Level_Game_Design.md](UNIT1_First_Level_Game_Design.md)。文件清楚區分「已完成」與「下一批必做」功能；不要把設計規格誤當成已實作。

完成標準：你能列出一局開始時的順序，以及水晶被打到 0 時現在會如何處理、還缺什麼勝敗流程。

### Unit 6：AI 與路徑規劃（最後才碰的難題）

| 優先 | 腳本 | 學什麼 | 建議切讀方式 |
| --- | --- | --- | --- |
| 28 | `Unit1RouteScanner.cs` | 格狀地圖、A*、優先佇列、避障 | 先讀地圖掃描，再讀 `FindPath`。 |
| 29 | `Unit1BotDuck.cs` | CPU 行為、目標評分、移動、戰鬥 | 先畫出「撿杖／攻擊／巡邏」決策。 |
| 30 | `EnemyDuckAI.cs` | 狀態機、巡邏、目標選擇、路徑、網路攻擊 | 最大檔案；一次只讀一個 state。 |

完成標準：先只增加一條可驗證規則，例如「低血 CPU 一定優先回水晶」，而不是先重寫全部 AI。

## Editor 腳本：第二輪才學

這些不是遊戲執行時邏輯，而是從 Unity 選單批次建置內容的工具。它們值得學，但應放在完成 Unit 0–3 之後。

| 分類 | 腳本 |
| --- | --- |
| 地圖與第一關建置 | `DuckMapBuilder.cs`、`FirstLevelDuckSetup.cs`、`Unit1MultiplayerContentBuilder.cs`、`Unit1MultiplayerContentPulse.cs` |
| 角色與素材整理 | `ConfigureDuckAnimator.cs`、`ConfigureDuckCharacters.cs`、`ReplaceDuck1Model.cs`、`ChineseUiFontSetup.cs` |
| 選角與多人原型建置 | `CharacterSelectionPrototypeBuilder.cs`、`FusionMultiplayerPrototypeBuilder.cs` |

讀法：先在 Git commit 或備份後執行一次建置選單，觀察場景哪些物件改變；再讀一小段建置程式。這類工具會一次修改很多資產，不適合拿來做第一個 C# 練習。

## 專案現在的學習邊界

這是「可完成的第一個多人作品」，不是要變成完整商業遊戲才算學完。依目前設計文件，下一批功能應維持以下順序：

1. 灰影被最後一擊轉化為陣營鴨。
2. 個人／陣營淘汰、勝負與重新開始。
3. CPU 決策接上水晶、敵我與轉化鴨。
4. 將一個重複查找或過大的類別拆出小元件，並補最小可行測試。

在做完這四項前，不建議再加入新地圖、新武器類型、帳號系統或排行榜；那些會擴大範圍，卻不會讓基礎理解更扎實。

## 你是否已具備足夠程度？

從專案已碰到的內容看，你已經在接觸 Unity 場景與 Prefab、C# 元件、UI、動畫、物理、多人同步、RPC、AI、A* 和 Editor 工具。這個範圍已超過入門教學專案；即使部分程式是協作或由工具生成，仍非常適合作為「理解、修改、驗證」能力的訓練場。

程式碼本身不能精準證明你每一段都能獨立寫出來，所以不要用「我看不懂最大 AI 腳本」判斷自己程度。若你可以完成 Unit 0–3 的小練習、在雙視窗驗證 Unit 4，並自行完成 Unit 5 的一個小規則，你就已能穩定進入 Unity 初階往中階的實作能力。
