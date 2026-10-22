# WaNut

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/banner-dark.png">
    <img src="docs/images/banner-light.png" alt="WaNut" width="624">
  </picture>
</p>

<p align="center"><a href="README.md">English</a> · <b>繁體中文</b></p>

WaNut 是一個 Windows 應用程式，點幾下就能開好自己的 Minecraft: Java Edition 伺服器：不用打指令、不用自己裝 Java、也不用改設定檔。

> 狀態：早期版本（v0.2.5）。已經可以用，但可能還有些小問題。

## 功能
- 用簡單的精靈建立 Vanilla、Paper、Fabric、Forge 或 NeoForge 伺服器（Minecraft 1.16.5 以上）
- 從模組包開始：Modrinth `.mrpack` 或 CurseForge 伺服器包會幫你裝好載入器、模組和設定
- 匯入世界，Fabric 世界用到的模組會一起裝好
- 依你選的 Minecraft 版本自動下載 Java
- 即時主控台、玩家列表，以及不會弄壞世界的安全關機
- 不用改檔案就能調整遊戲設定、白名單和管理員（OP）名單；其他選項放在「更多設定」和進階模式
- 每次啟動前自動備份世界，可以還原，也有清理規則
- 透過區域網路或虛擬區網（Radmin VPN、ZeroTier、Tailscale、Hamachi）和朋友一起玩，防火牆規則會幫你加好
- 英文和繁體中文介面、淺色和深色主題，還有一顆像素花生陪你
- 透過 GitHub Releases 自動更新

完整設計請見 [docs/blueprint.md](docs/blueprint.md)（英文）。

## 認識花生
伺服器在跑的時候，有一顆小小的像素花生陪著你。

<p align="center">
  <img src="docs/images/guide-hello.gif" alt="花生揮手打招呼" width="96">
  <img src="docs/images/starting.gif" alt="伺服器啟動中" width="96">
  <img src="docs/images/online.gif" alt="伺服器上線" width="96">
  <img src="docs/images/friends.gif" alt="朋友加入" width="96">
  <img src="docs/images/backup-done.gif" alt="備份完成" width="96">
  <img src="docs/images/downloading.gif" alt="下載中" width="96">
</p>

## 安裝
從[最新版本](https://github.com/PeanutGT/WaNut/releases/latest)下載 `WaNut-win-Setup.exe` 並執行。WaNut 只安裝給你自己的 Windows 帳號，所以不需要系統管理員權限，有新版本時也會自己更新。你的伺服器和世界存放在 `%APPDATA%\WaNut`，更新或解除安裝 WaNut 時都會保留。安裝程式目前還沒有數位簽章，所以第一次執行時 Windows SmartScreen 可能會跳出警告：請按「其他資訊」，再按「仍要執行」。

## 從原始碼建置
需要 Windows 和 .NET 10 SDK。

```
dotnet build
dotnet test
```
