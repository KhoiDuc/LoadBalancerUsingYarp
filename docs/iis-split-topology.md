# IIS Split Topology — Scenario 1 & 3

Hai kieu trien khai da chon. Cac option cu (1-6) van giu nguyen.

## Scenario 1 — Tat ca tren 1 server (199.30.30.91)
LB + node1 + node2 nam chung tren `199.30.30.91`. LB goi node qua loopback.

**Cach lam:** dung menu **3** (`setup-iis-server.ps1`) nhu cu.
- Node bind `127.0.0.1` (khong lo ra ngoai), LB bind `*:5266` (public).
- Destinations LB = `http://127.0.0.1:5001/` va `:5002/` (mac dinh trong appsettings.json).
- Truy cap: `http://199.30.30.91:5266` (nho `-OpenFirewall` + Security Group cho 5266).

```powershell
# TREN SERVER 199.30.30.91 (PowerShell Admin, thu muc scripts)
.\iis-run.ps1 publish.ps1
.\iis-run.ps1 setup-iis-server.ps1 -BackendVariant redis -OpenFirewall
```

## Scenario 3 — Node o server, LB o may local
node1/node2 chay tren `199.30.30.91` (mo ra ngoai), con LB chay tren may ban va tro nguoc ve server.

Chieu ket noi la **LB(local) -> node(server)**. Server co IP public nen goi vao duoc; chi can mo firewall port 5001/5002 phia server.

### Buoc 1 — TREN SERVER 199.30.30.91: deploy node (menu 7)
```powershell
.\iis-run.ps1 publish.ps1
.\iis-run.ps1 setup-nodes.ps1 -BackendVariant redis -OpenFirewall
```
- Node bind `*` (mac dinh) + mo firewall 5001/5002.
- Neu server sau NAT/cloud: mo them Security Group/inbound cho 5001, 5002.

### Buoc 2 — TREN MAY LOCAL: deploy LB (menu 8)
Menu 8 se hoi IP server roi tu dien vao. Hoac chay tay:
```powershell
.\iis-run.ps1 setup-lb.ps1 -Node1 http://199.30.30.91:5001/ -Node2 http://199.30.30.91:5002/
```
- Dia chi node duoc nap qua ENV VAR, ghi de `appsettings.json` (khong sua file, khong build lai).
- Them `-Https` neu muon self-signed HTTPS tren may local.
- Truy cap LB: `http://localhost:5266`. Xem header `X-Served-By` de biet node nao phuc vu.

## Dung voi app publish san (ngoai lab)

Mot folder publish → **ca hai** node1 và node2 (khong tach 2 folder). Lab **khong** kiem tra / cau hinh Redis cua app ngoai.

**Scenario 1 (1 may)** — menu **3**, nhap AppPath (Enter trong = demo lab):

```text
dotnet publish YourApp -c Release -o D:\Publish\ClientPaymentPortal
iis-setup.bat → 1 (can publish\lb) → 3 → AppPath = D:\Publish\ClientPaymentPortal
```

Hoac PowerShell:

```powershell
.\iis-run.ps1 setup-iis-server.ps1 -AppPath D:\Publish\ClientPaymentPortal
```

Khi co `-AppPath`, LB tu bat **Affinity** (sticky cookie) — an toan khi chua biet app co share session/DataProtection hay khong.

**Scenario 3** — menu **7** (+ AppPath) tren server, menu **8** tren may LB: chon Mode **Affinity** (Enter = Stateless cho demo).

```powershell
# Tren server
.\iis-run.ps1 setup-nodes.ps1 -AppPath D:\Publish\ClientPaymentPortal -OpenFirewall
# Tren may LB
.\iis-run.ps1 setup-lb.ps1 -Node1 http://199.30.30.91:5001/ -Node2 http://199.30.30.91:5002/ -Mode Affinity
```

Luu y:

1. App that thuong khong in `NodeName` tren UI — xem header **`X-Served-By`** (LB gan).
2. Muon **Stateless** that su ma khong mat dang nhap → app phai tu share state (Redis / DataProtection chung, …), hoac truyen `-Mode Stateless` co chu dich.
3. Neu portal kiem `AllowedHosts` chat, YARP forward Host goc — co the can noi cau hinh phia portal.

## Ghi chu
- `setup-nodes.ps1` va `setup-lb.ps1` deu xai lai App Pool "No Managed Code", cap quyen IIS_IUSRS, va `cleanup-iis.ps1` (menu 5) van xoa duoc node1/node2/yarp-lb nhu cu.
- Ban redis (demo lab): dam bao ca node lan (neu can) LB toi duoc Redis theo `appsettings.Production.json`.
- Scenario 2 (node o may local, LB o server) da bo qua vi may local thuong sau NAT, server khong goi nguoc ve duoc neu khong co VPN/tunnel/port-forward.
