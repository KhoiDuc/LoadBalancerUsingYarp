# Setup tren IIS SERVER (1 server, khac port)

`yarp-lb` (5266, public) + `node1` (5001) & `node2` (5002) chi lang nghe `127.0.0.1`.
Env = Production -> backend doc `appsettings.Production.json` (Redis `199.30.30.91:6380`).

## 1. Cai dat truoc
- **.NET 10 Hosting Bundle** + IIS. `iisreset` sau khi cai.
- Server phai ket noi duoc `199.30.30.91:6380` (mo route/firewall phia Redis).

## 2. Copy source + publish
Copy repo len server, hoac publish o may build roi copy thu muc `publish\` len.
```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

## 3. Setup (tu dong)
Run as Administrator:
```powershell
# HTTP thuan
powershell -ExecutionPolicy Bypass -File scripts\setup-iis-server.ps1 -OpenFirewall

# HTTPS voi cert THAT (da import vao LocalMachine\My)
powershell -ExecutionPolicy Bypass -File scripts\setup-iis-server.ps1 `
    -HostName "lb.example.com" -CertThumbprint "ABCD...1234" -HttpsPort 443 -OpenFirewall
```
Script se: tao 3 site/pool (No Managed Code), env Production, backend bind `127.0.0.1`,
cap quyen doc thu muc publish cho `IIS AppPool\<name>`, gan HTTPS (neu co cert), mo firewall
cho LB (khong mo node1/node2 ra ngoai).

Lay thumbprint cert:
```powershell
Get-ChildItem Cert:\LocalMachine\My | Format-List Subject, Thumbprint
```

## 4. Setup thu cong (tom tat)
Giong local nhung luu y phan "server":
1. App Pool No Managed Code; env `ASPNETCORE_ENVIRONMENT=Production`, `NODE_NAME=node1/node2`.
2. Binding node1/node2 chon IP `127.0.0.1` (khong public).
3. Cap quyen: thu muc publish > Security > them `IIS AppPool\node1` (va node2, yarp-lb) quyen Read & Execute.
4. LB: binding https + chon cert that (SNI neu nhieu hostname).
5. Firewall chi mo port LB (443/5266).

## 5. Van hanh (production)
- **Auto-start**: App Pool `startMode=AlwaysRunning`, `idleTimeout=0` (script da set).
  Nen cai them **Application Initialization** de warm-up sau khi recycle.
- **DataProtection**: dung backend-redis -> key luu tren Redis, chia se giua node, ben qua recycle.
  `SetApplicationName("LbDemo")` phai giong het nhau tren moi node (da co san trong code).
- **HTTPS**: bat HSTS, dung cert that; khong dung self-signed.
- **Logging**: bat ANCM stdout log tam khi debug, tat khi on dinh.
- **Redis**: neu can ben vung, bat AOF/RDB persistence phia Redis server.

## 6. Kiem tra
```powershell
curl.exe -i http://localhost:5266/         # tren chinh server
curl.exe -i https://lb.example.com/        # tu ngoai (neu co cert)
```
Header `X-Served-By` doi node1/node2; "Session tao luc" giu nguyen (do share Redis).

## Xem them
- Muon **tach LB va node ra 2 may** (node o server, LB o may local): xem
  [iis-split-topology.md](iis-split-topology.md) (menu 7 + 8 trong `iis-setup.bat`).
