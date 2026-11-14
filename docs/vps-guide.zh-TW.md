# 用自己的 VPS 跑 WaNut 伺服器

[English](vps-guide.md)

WaNut 可以把伺服器放在 Linux VPS（租用的雲端電腦）上跑，不用開著你的電腦。這樣你關機時朋友也能玩，而且每次停止後世界都會存回你的電腦。這是進階模式的功能。

你需要：
- 一台 **Ubuntu 或 Debian**（x64 或 ARM64）的 VPS，**記憶體至少 2 GB**。1 GB 的主機太小，扣掉 Linux 後 Java 拿不到足夠的記憶體。
- SSH 登入方式：金鑰檔（建議）或密碼。
- VPS 的費用由你和廠商自理；WaNut 不會幫你建立、關閉主機或收費。

## 免費方案：Oracle Cloud Always Free

Oracle 的永久免費方案有一台 ARM（Ampere A1）主機，**2 核心、12 GB 記憶體**，原版或 Paper 給 4–8 人玩很夠。網路上舊教學寫 4 核 / 24 GB，Oracle 在 2026 年 7 月已經縮減。

先知道這些：
- 註冊要信用卡驗證身分（會有一筆暫時預授權）。30 天試用結束後，永久免費的主機仍然免費。
- 主機只能開在註冊時選的 **home region**，之後不能改。熱門區域的 A1 常常顯示「out of capacity」，要換時段多試幾次。
- Oracle 可能會**收回閒置的免費主機**（7 天內 CPU、網路、記憶體都低於 20%）。世界都存在你的電腦，不會遺失，只是可能要重新建立主機。
- x86 的小型「Micro」主機（1 GB）太小，跑不動 Minecraft。

### 1. 建立主機（在 Oracle Cloud 網站）
1. 到 **Compute > Instances > Create instance**。
2. **Image：** 先選 **Ubuntu**（22.04 或 24.04）。如果清單只看到「Minimal aarch64」，要先選映像檔再選 Shape。
3. **Shape：** **Ampere > VM.Standard.A1.Flex**，2 OCPU、12 GB 記憶體。
4. **SSH keys：** 選 **Generate a key pair for me**，並**下載私鑰**（`.key` 檔），好好保存。
5. 建立後，在主機頁面複製 **Public IP address**。

### 2. 加進 WaNut（在你的電腦）
1. 打開進階模式，到 **App 設定 > 我的 VPS > 新增**。
2. 位址：公用 IP。連接埠：22。使用者名稱：`ubuntu`。
3. 登入方式選 **我已經有的私鑰檔**，選剛剛下載的 `.key` 檔。
4. 按 **測試連線**，確認顯示的主機指紋，再按 **儲存**。
5. 保持勾選 **伺服器啟動時，自動在這台 VPS 的防火牆開連接埠**（或第一次啟動時 WaNut 問你時選「是」）。Oracle 的 Ubuntu 映像檔會擋掉 SSH 以外的所有連接埠；WaNut 會替你的伺服器連接埠加一條規則，並存起來讓重開機後也有效。
6. 到該伺服器的 **伺服器分頁 > 進階**，把 **在哪裡執行** 改成這台 VPS，然後啟動。

### 3. 在 Oracle 的 Security List 開連接埠（在 Oracle Cloud 網站）
這是 Oracle 擋在主機前面的防火牆。WaNut 改不了它（那需要你的 Oracle 帳號金鑰）。
1. 先到 WaNut 的 **連線分頁** 看伺服器的連接埠。不一定是 25565：有好幾台伺服器時，WaNut 會給每台不同的連接埠（例如 25566）。
2. 在主機頁面點 **子網路（subnet）**，再到 **Security > Default Security List > Add Ingress Rules**。
3. 填：
   - **Source CIDR：** `0.0.0.0/0`
   - **IP Protocol：** TCP
   - **Source Port Range：** **留空**
   - **Destination Port Range：** 你的伺服器連接埠，例如 `25566`

   ⚠️ 兩個連接埠欄位就在隔壁，很容易填錯。填在 **Source** Port Range 的規則看起來像成功了，其實會擋掉所有朋友。
4. 儲存。回到 WaNut 連線分頁按 **再檢查一次**，應該會顯示朋友連得到。

## 朋友連不進來時
伺服器開好後，WaNut 會從你的電腦檢查一次位址，結果顯示在連線分頁：
- **VPS 拒絕了連線：** VPS 自己的防火牆擋住了。讓 WaNut 自動開（第 2 段第 5 步），或把連線分頁顯示的指令**在 VPS 的 SSH 視窗裡**執行。
- **VPS 沒有回應**（朋友看到「Connection timed out」）：雲端防火牆擋住了。檢查第 3 段，尤其是 Destination Port Range。

## 自己用 SSH 連線（選用）
在**你電腦的 PowerShell** 執行，不是在 SSH 視窗裡：
```
ssh -i "$HOME\Downloads\ssh-key-xxxx.key" ubuntu@你的公用IP
```
如果 Windows 說 `UNPROTECTED PRIVATE KEY FILE`，先修正金鑰檔權限（一樣在你電腦的 PowerShell）：
```
icacls "$HOME\Downloads\ssh-key-xxxx.key" /inheritance:r
icacls "$HOME\Downloads\ssh-key-xxxx.key" /grant:r "$($env:USERNAME):R"
```
像 `sudo iptables -S INPUT` 這類指令是**在 VPS 上**、SSH 視窗裡執行。

## 其他廠商
任何 Ubuntu 或 Debian 的 VPS 都一樣能用，記得在廠商的防火牆開伺服器的 TCP 連接埠：AWS 是 **Security Group**、Azure 是 **網路安全性群組**、Google Cloud 是 **防火牆規則**。學生可以申請 Azure for Students（US$100 額度，不需信用卡）。
