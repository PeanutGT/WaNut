# Run a WaNut server on your own VPS

[繁體中文](vps-guide.zh-TW.md)

WaNut can run a server on a Linux VPS (a rented computer in a data center) instead of your PC. Friends can then join while your PC is off, and your worlds come back to your PC after each stop. This is an Advanced mode feature.

You need:
- A VPS with **Ubuntu or Debian** (x64 or ARM64) and **at least 2 GB of RAM**. A 1 GB host is too small: Java can't get enough memory next to Linux.
- An SSH sign-in: a key file (recommended) or a password.
- You pay the VPS provider yourself; WaNut never creates, stops or bills hosts.

## Free option: Oracle Cloud Always Free

Oracle's Always Free plan includes an ARM (Ampere A1) host with **2 cores and 12 GB of RAM**, enough for 4–8 players on Vanilla or Paper. Older guides say 4 cores / 24 GB; Oracle cut it in July 2026.

Good to know first:
- Signing up needs a credit card to check who you are (a small temporary hold). Always Free hosts stay free after the 30-day trial.
- Hosts can only be created in your **home region**, chosen at sign-up and fixed after. Popular regions are often "out of capacity" for A1; try again later or another time of day.
- Oracle may **take back an idle Always Free host** (CPU, network and memory all under 20% for 7 days). Your worlds are safe on your PC, but you may have to create the host again.
- The tiny x86 "Micro" hosts (1 GB) are too small for Minecraft.

### 1. Create the host (Oracle Cloud website)
1. Go to **Compute > Instances > Create instance**.
2. **Image:** choose **Ubuntu** (22.04 or 24.04) first. The list may only show "Minimal aarch64" until you pick the image before the shape.
3. **Shape:** **Ampere > VM.Standard.A1.Flex**, 2 OCPU and 12 GB memory.
4. **SSH keys:** choose **Generate a key pair for me** and **download the private key** (a `.key` file). Keep it safe.
5. Create it, then copy the **Public IP address** from the instance page.

### 2. Add it to WaNut (on your PC)
1. Turn on Advanced mode, then open **App settings > My VPS > Add**.
2. Address: the public IP. Port: 22. User name: `ubuntu`.
3. Sign in with **A private key file I already have**, and pick the `.key` file you downloaded.
4. Press **Test connection**, check the host key it shows, then **Save**.
5. Leave **Open my servers' ports in this VPS's firewall** ticked (or answer yes when WaNut asks on the first start). Oracle's Ubuntu images block every port but SSH; WaNut adds a rule for your server's port and saves it so it survives a reboot.
6. In the server's **Server tab > Advanced**, set **Where it runs** to this VPS, then Start.

### 3. Open the port in Oracle's Security List (Oracle Cloud website)
This is Oracle's own firewall in front of the host. WaNut can't change it (that would need your Oracle account keys).
1. Look up your server's port in WaNut's **Connect tab**. It is not always 25565: when several servers exist, WaNut gives each one its own port (for example 25566).
2. On the instance page, click the **subnet**, then **Security > Default Security List > Add Ingress Rules**.
3. Fill in:
   - **Source CIDR:** `0.0.0.0/0`
   - **IP Protocol:** TCP
   - **Source Port Range:** **leave empty**
   - **Destination Port Range:** your server's port, such as `25566`

   ⚠️ The two port fields sit next to each other. A port typed into **Source** Port Range looks like a working rule but blocks every friend.
4. Save. In WaNut's Connect tab, press **Check again**: it should say friends can reach the address.

## When friends can't join
WaNut checks the address from your PC once the server is up and says what it found on the Connect tab:
- **The VPS turned the connection away:** the VPS's own firewall blocks the port. Let WaNut open it (step 2.5), or run the commands the Connect tab shows **in an SSH window on the VPS**.
- **The VPS didn't answer** (friends see "Connection timed out"): the cloud firewall blocks it. Check step 3, especially the Destination Port Range.

## Connecting with SSH yourself (optional)
Run this in **PowerShell on your PC**, not inside an SSH window:
```
ssh -i "$HOME\Downloads\ssh-key-xxxx.key" ubuntu@YOUR_PUBLIC_IP
```
If Windows says `UNPROTECTED PRIVATE KEY FILE`, fix the key file's permissions first (also in PowerShell on your PC):
```
icacls "$HOME\Downloads\ssh-key-xxxx.key" /inheritance:r
icacls "$HOME\Downloads\ssh-key-xxxx.key" /grant:r "$($env:USERNAME):R"
```
Commands such as `sudo iptables -S INPUT` run **on the VPS**, inside the SSH window.

## Other providers
Any Ubuntu or Debian VPS works the same way. Open the server's TCP port in the provider's firewall: AWS **Security Group**, Azure **Network security group**, Google Cloud **Firewall rules**. Students can get Azure for Students credit (US$100, no card).
