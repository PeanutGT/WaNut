# WaNut 開發規範

這份文件是 WaNut 所有開發工作（人、Claude、Gemini）都要遵守的規則。`CLAUDE.md` 和 `GEMINI.md` 都指向這份文件。規則有變動時，先改這份文件，再改程式。

## 1. 分支與 PR 流程
- `main` 永遠保持可編譯、測試全過。不直接推送到 `main`。
- 每個開發步驟一個分支，命名：
  - 開發步驟：`step-<編號>-<簡述>`，例如 `step-1-skeleton`
  - 其他：`feat/<簡述>`、`fix/<簡述>`、`docs/<簡述>`
- 每個分支開一個 PR，由 Peanut 審查與合併（Squash merge）。Claude 不自行合併。
- PR 說明包含：改了什麼（Before / After）、怎麼測試的、介面變動附截圖。
- Commit 訊息、PR 標題與 PR 說明一律用英文。

## 2. Commit 規則
- 使用 [Conventional Commits](https://www.conventionalcommits.org/) 格式，以英文撰寫：
  `feat:`、`fix:`、`docs:`、`test:`、`refactor:`、`chore:`、`ci:`
  例如：`feat: download Temurin JRE from Adoptium`
- 一個 commit 只做一件事。
- **不加 `Co-Authored-By` 或任何 AI（Claude、Gemini 等）署名**，AI 不出現在貢獻者名單。
- Commit 作者為 Peanut 本機的 Git 帳號。

## 3. 程式碼風格（C# / .NET 10）
- 開啟 Nullable，警告視為錯誤（`TreatWarningsAsErrors`）。
- 使用 file-scoped namespace，一個檔案一個型別。
- 命名：型別、方法、屬性、常數與 `static readonly` 欄位用 PascalCase；其他私有欄位用 `_camelCase`；非同步方法以 `Async` 結尾（測試方法除外，見第 7 節）。
- 所有檔案、網路 I/O 都用 async，並接受 `CancellationToken`；不使用 `.Result` / `.Wait()`。
- 程式碼、識別名稱、註解用英文；註解只寫「為什麼」，不重複程式碼在做什麼。
- 格式以 `.editorconfig` 為準，PR 前執行 `dotnet format`。

## 4. 架構
- `WaNut.Core`：所有邏輯，不可引用 WPF 或任何介面元件。
- `WaNut.App`：WPF，採 MVVM（CommunityToolkit.Mvvm）。code-behind 只放純畫面邏輯。
- 服務透過相依性注入（Microsoft.Extensions.DependencyInjection）取得。
- 全程式共用一個 `HttpClient`，User-Agent 為 `WaNut/<版本> (github.com/PeanutGT/WaNut)`。
- 新增 NuGet 套件前先在 PR 說明理由。

## 5. 介面文字
- 所有顯示給使用者的文字放在 `Resources/Strings.resx`，程式中不寫死字串。
- 預設英文；之後新增 `Strings.zh-TW.resx` 即完成繁中翻譯。
- 資源鍵命名：`<畫面>_<元件>`，例如 `CreateServer_Title`。
- XAML 用 `{res:Text CreateServer_Title}` 讀取，C# 用 `Text.Get("CreateServer_Title")`。鍵打錯時畫面會顯示 `[鍵名]`。

## 6. 安全與資料保護
- 關閉伺服器一律先送 `stop` 並等待結束；強制結束只在逾時後、經使用者同意才執行。
- 所有下載必須走 HTTPS 並驗證雜湊值（SHA-1 / SHA-256）。
- 任何會刪除或覆蓋世界存檔的動作，執行前必須先備份。
- 設定檔採「寫入暫存檔再改名」，避免寫到一半損壞。
- Repo 中不放任何金鑰、密碼或個人資料。

## 7. 測試
- 測試框架：xUnit。`WaNut.Core` 的邏輯都要有單元測試。
- 測試命名：`方法_情境_預期結果`，例如 `Resolve_Version1165_ReturnsJava8`。
- 開 PR 前 `dotnet build` 與 `dotnet test` 必須全過。
- 不可跳過、停用或刪除測試來讓 CI 通過。
- 需要網路的測試標記為 `[Trait("Category", "Network")]`，CI 預設不跑。

## 8. 版本與發佈
- 版號採語意化版本（SemVer），開發期為 `0.x.y`。
- 發佈時打 tag `vX.Y.Z`，在 GitHub Releases 附上安裝程式與更新說明。

## 9. 文件
- 產品設計與決策記錄在 `docs/blueprint.md`，決策改變時同步更新。
- 本規範：`docs/CONVENTIONS.md`。
- 所有文件（.md）放在 `docs/`。只有 `README.md`、`CLAUDE.md`、`GEMINI.md` 留在根目錄，因為 GitHub、Claude 和 Gemini 只會從根目錄讀取它們。
