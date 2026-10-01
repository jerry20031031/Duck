# UNIT1：Fusion 角色與敵人不移動的除錯紀錄

日期：2026-09-29  
範圍：BigHall 進入 UNIT1 後的本機玩家與灰影鴨子敵人。

## 現象

- 玩家進入 UNIT1 後，WASD 沒有效果；跳躍也異常。
- 三隻灰影鴨子同樣不巡邏，像停在原地或飄著。
- 初期曾出現出生高度、鏡頭、角色外觀延續等其他問題；但「玩家和敵人都無法走路」是另一個共同的移動問題。

## 關鍵證據

Console 顯示：

```text
[UNIT1-DIAG Player] ... stateAuthority=True ... moverEnabled=True ...
[UNIT1-DIAG Enemy] ... stateAuthority=True | kinematic=False | gravity=True
[UNIT1-DIAG Enemy] patrol/chase movement started ... velocity=(-0.39, 0.00, 1.13)
```

這三行很重要：

1. 玩家不是因為沒有 StateAuthority 而不能控制。
2. 敵人不是因為 AI 沒有執行；它已經算出巡邏目標與速度。
3. 速度已被寫入，卻沒有看見座標前進。因此下一步不能再猜輸入、敵人目標或 Collider，而要檢查「速度到座標」這一段。

## 原本的錯誤做法

一開始讓 `DuckMover.FixedUpdate()` 寫 `Rigidbody.linearVelocity`，同時角色上有 Fusion `NetworkTransform`。

後來雖然把移動改進 `FixedUpdateNetwork()`，仍然只寫 `linearVelocity`。這只修正了 **誰在什麼 tick 寫速度**，沒有修正 **誰負責產生並同步最終座標**。

在這個專案的 Fusion 場景流程中，Unity Rigidbody 的速度積分與 Fusion `NetworkTransform` 的狀態擷取／套用是不同的時序。結果是速度有值，但角色最後沒有可靠地前進；玩家和敵人都使用同一模式，所以一起失效。

> 不要把「Console 顯示 velocity 非零」當成「角色一定會移動」。一定要再驗證 position 是否真的改變。

## 最終架構：Fusion tick 的 kinematic motor

目前改為下列單一路徑：

```text
玩家輸入／敵人 AI
        ↓
FixedUpdateNetwork（僅 StateAuthority）
        ↓
DuckKinematicMotor.Move
  ├─ Rigidbody.SweepTest：先檢查牆、物件、其他 Collider
  └─ Rigidbody.position：直接提交安全的位移
        ↓
NetworkTransform 同步這個最終 Transform
```

- Rigidbody 保持 `isKinematic = true`、`useGravity = false`；它是碰撞殼，不再交給 Unity 自動積分速度。
- `DuckKinematicMotor` 先用 `SweepTest` 找出阻擋物，再將位置移到碰撞前的安全距離，因此外牆與地圖物件仍有碰撞效果。
- 玩家在 `DuckMover` 內自己管理垂直速度、重力、跳躍與地面偵測；地面偵測不限定圖層，任何有效 Collider 都可成為落腳處。
- 敵人使用同一個 motor，所以玩家與敵人的基礎移動行為一致。

## 對應檔案

| 工作 | 檔案 |
| --- | --- |
| 共用碰撞移動 | `Assets/Scripts/DuckKinematicMotor.cs` |
| 玩家輸入、跳躍、手動重力 | `Assets/Scripts/DuckMover.cs` |
| 玩家 StateAuthority gate 與 Fusion tick | `Assets/Scripts/FusionDuckPlayer.cs` |
| 敵人巡邏、追蹤與移動 | `Assets/Scripts/EnemyDuckAI.cs` |

## 下次遇到「角色不動」的檢查順序

1. **先找共同點。** 如果玩家與敵人同時不動，優先查共用移動層、物理設定或場景生命週期，不要先只修玩家輸入。
2. **分開驗證四件事：**
   - `FixedUpdateNetwork()` 是否在跑？
   - `HasStateAuthority` 是否為 `true`？
   - 輸入／AI 是否產生目標位移？
   - `transform.position` 是否真的在下一個 tick 改變？
3. **Collider 與地板是下一層。** 只有在座標真的改變但被彈回、卡住或下墜時，才查牆、地板、出生點與 Capsule 尺寸。
4. **不要讓兩套系統都寫位置。** Unity `FixedUpdate`、Fusion `FixedUpdateNetwork`、NetworkTransform 校正、場景轉移定位，必須明確指定唯一擁有者。
5. **重新 Play 驗證。** 改變 NetworkObject、Rigidbody 或場景轉移後，先 Stop 再 Play，避免舊的網路物件狀態干擾結果。

## 這次最值得記住的一句話

**在網路遊戲裡，速度值正確不等於角色位置會正確；要追到最後真正寫入並同步 Transform 的那一行。**
