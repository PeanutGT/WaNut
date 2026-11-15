# WaNut

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/banner-dark.png">
    <img src="docs/images/banner-light.png" alt="WaNut" width="624">
  </picture>
</p>

<p align="center">
  <a href="https://github.com/PeanutGT/WaNut/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/PeanutGT/WaNut/ci.yml?branch=main&label=CI" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/PeanutGT/WaNut" alt="License: GPL-3.0"></a>
  <a href="https://github.com/PeanutGT/WaNut/releases/latest"><img src="https://img.shields.io/github/v/release/PeanutGT/WaNut?color=e07a3a" alt="Latest release"></a>
  <a href="https://github.com/PeanutGT/WaNut/releases"><img src="https://img.shields.io/github/downloads/PeanutGT/WaNut/total?color=8a5a3c" alt="Downloads"></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078d4" alt="Windows">
  <img src="https://img.shields.io/badge/Minecraft-1.16.5%2B-62b47a" alt="Minecraft 1.16.5+">
  <img src="https://img.shields.io/badge/.NET-10-512bd4" alt=".NET 10">
</p>

<p align="center"><a href="README.md">English</a> · <b>繁體中文</b></p>

WaNut 是一個 Windows 應用程式，點幾下就能開好自己的 Minecraft: Java Edition 伺服器：不用打指令、不用自己裝 Java、也不用改設定檔。

> 狀態：早期版本。已經可以用，但可能還有些小問題。

## 功能
**建立**
- 用簡單的精靈建立 Vanilla、Paper、Fabric、Forge 或 NeoForge 伺服器（Minecraft 1.16.5 以上）
- 從模組包開始：Modrinth `.mrpack` 或 CurseForge 伺服器包會幫你裝好載入器、模組和設定
- 匯入世界，Fabric 世界用到的模組會一起裝好
- 依你選的 Minecraft 版本自動下載 Java
- 可以同時跑好幾個伺服器，每個都會自動分到空的連接埠

**運行**
- 即時主控台、不會弄壞世界的安全關機，以及有 CPU 和記憶體圖表的「效能」分頁
- 效能小幫手：建議的視野與模擬距離、沒人在線卻一直吃 CPU 時會提醒你、Paper 的「減輕生物與村民負擔」開關，以及一鍵產生 spark 效能報告
- 有玩家頭像的玩家列表，選單裡就能踢出、封鎖、設為管理員（OP）或加入白名單
- 不用改檔案就能調整遊戲設定、白名單、管理員和封鎖名單；其他選項放在「更多設定」和進階模式
- 用卡片瀏覽 Modrinth 模組並排序，在詳情視窗挑版本，一次裝好幾個或直接拖進自己的 jar，一鍵更新；不相容的模組安裝前會先提醒，也能暫時停用模組而不用刪掉
- 「資料包」分頁管理 Modrinth 資料包和伺服器資源包
- 伺服器當機時，WaNut 會用白話說明原因，並提供一鍵修復按鈕

**保護存檔**
- 每次啟動前自動備份世界，伺服器運行時也會定時備份
- 可以還原整個備份，或只還原單一維度；清理規則讓備份不會塞滿硬碟

**和朋友一起玩**
- 透過區域網路或虛擬區網（Radmin VPN、ZeroTier、Tailscale、Hamachi），防火牆規則會幫你加好
- 透過網際網路：用 UPnP 自動在路由器開連接埠，不行的話改用 playit.gg 通道
- 分享附有連線位址的邀請卡，並把伺服器動態發到 Discord
- 也可以透過 SSH 在你自己的 Linux VPS 上跑伺服器：WaNut 會在那裡裝好 Java、上傳伺服器、在 VPS 的防火牆開連接埠（會先問你）、檢查朋友連不連得到，關掉 WaNut 後伺服器也會繼續跑。請見 [VPS 教學](docs/vps-guide.zh-TW.md)

**貼心**
- 英文和繁體中文介面、淺色和深色主題，還有一顆會帶你導覽的像素花生
- 安靜的動態效果，只在有變化時才動：分頁滑動、橫幅展開、按鈕會被按下去，伺服器上線時花生會跳一下；會跟著 Windows 的動畫設定
- 「要刪除這個伺服器嗎？」這類問題改成視窗內的面板，不再跳出新視窗；標題使用圓體字
- 顯示下載剩餘時間，長時間的工作完成時會通知你
- 透過 GitHub Releases 自動更新

完整設計請見 [docs/blueprint.md](docs/blueprint.md)（英文）。

## 認識花生
伺服器在跑的時候，有一顆小小的像素花生陪著你。

<p align="center">
  <img src="docs/images/peanut-faces.png" alt="花生：平靜、開心、緊張、想睡、過熱" width="528">
</p>

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

## 授權
WaNut 是自由軟體，採用 [GNU 通用公共授權條款第 3 版（GPL-3.0）](LICENSE)。你可以使用、研究、修改和分享它；如果分享修改過的版本，也必須以相同授權條款開放原始碼。

標題字型使用[昭源環方 Chiron GoRound TC](https://github.com/chiron-fonts/chiron-go-round-tc)，採用 [SIL 開放字型授權 1.1](src/WaNut.App/Assets/Fonts/ChironGoRoundTC-LICENSE.txt)。

WaNut 不是 Minecraft 官方產品，也未經 Mojang 或 Microsoft 認可或與其有關。WaNut 不包含 Minecraft 本體：伺服器檔案會在你的電腦上從官方來源下載。
