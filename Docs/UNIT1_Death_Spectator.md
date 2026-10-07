# UNIT1 死亡觀戰與墓碑（2026-10-06）

## 遊戲行為

- 真人、CPU 陣亡後隱藏身體、姓名／陣營文字與碰撞體；死亡落點留下圓頂灰色 RIP 墓碑，不阻擋移動。
- 墓碑落點／朝向及死亡狀態由 Fusion StateAuthority 同步。網路角色物件保留，以維持存活人數、勝負與掉杖判定；墓碑不跟著角色插值漂移。
- 只有 `Runner.LocalPlayer` 映射的真人啟用觀戰。優先真人隊友，其次 CPU、己方轉化鴨，不選敵方或中立灰影。
- `←`／`→` 或下方「上一位／下一位」切換；保留右鍵旋轉與滾輪縮放。目標死亡、離場或改為敵方時，每 0.35 秒更新並自動換人。
- 無存活隊友時觀看己方存活水晶；沒有水晶時觀看固定死亡落點。對局結束關閉觀戰 HUD，讓結算畫面接手。
- 死亡取消尚未結算的真人攻擊；手杖沿用現有釋放流程，模型不隨鴨身隱藏。本次沒有更改掉杖的落點規則。
- 灰影被擊倒仍依原本規則「轉化／重新轉化」，不是角色陣亡，不產生墓碑。

## 實作入口

- `Assets/Scripts/Networking/FusionDuckPlayer.cs`：真人死亡狀態／落點、觀戰安裝。
- `Assets/Scripts/AI/Unit1BotDuck.cs`：CPU 死亡落點及墓碑。
- `Assets/Scripts/Characters/DuckGraveMarker.cs`：純本機墓碑與隱藏／還原呈現，排除手上手杖。
- `Assets/Scripts/Camera/Unit1SpectatorController.cs`：隊友選擇、鏡頭、HUD 與退路。
- `Assets/Scripts/Combat/DuckWandAttack.cs`：死亡拒絕攻擊、取消前搖中的協程。

## 回歸測試

Unity 編輯模式、場景無未儲存修改時，選 `Tools > UNIT1 Tests > Death Spectator Regression`。

測試使用真實 UNIT1 場景、實際 prefab、離線 Fusion Single runner。只在明確執行此測試時建立測試角色／暫停測試 CPU；不修改場景資產、正常 Play 不會啟用測試。近距離畫面檢查保留 5 秒，完成後停止 Play 並恢復原本場景，不關閉 Unity。

已驗證：墓碑貼地、身體／碰撞消失、墓碑唯一且不漂移、恢復呈現、手杖模型排除、真人死亡停移動／停攻擊／掉杖、只有本機玩家擁有相機、隊友優先順序、切換循環、目標陣亡自動換人、轉化改陣營後移除、己方水晶／固定死亡點退路、結算時隱藏觀戰 HUD。另以 Unity 畫面確認墓碑、中文 HUD 與按鈕切換；方向鍵使用 Input System 虛擬鍵盤回歸測試。

尚未實測：多台電腦 Shared 房間、遠端死亡複製、晚加入、房主轉移。本次只有單機 Fusion 實測，不宣稱跨機連線已驗證。

最終結果：Unity 6000.0.55f1 編譯通過，呈現 fixture 8 項＋真實場景 31 項檢查通過，共 39 項。自動換人測試等待實際刷新條件，不以固定 450ms 假設編輯器幀率；也檢查場景轉移覆寫鏡頭後能恢復觀戰目標。
