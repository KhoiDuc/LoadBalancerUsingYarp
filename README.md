# yarp-lb-lab

Mot lab nho de **hoc va test load balancing** bang [YARP](https://github.com/dotnet/yarp)
tren .NET 10, kem huong dan chay tren **IIS local & IIS server** (co ca ban **IIS ARR** de so sanh).
Mot Load Balancer dung chung + cac backend demo (in-memory & Redis, login, realtime SignalR, file upload) de thay ro
**tai sao app nhieu node can share state**.

```
            http://localhost:5266  (LoadBalancer / YARP)
                         |
            +------------+------------+
            |                         |
  http://localhost:5001      http://localhost:5002
     Backend node1              Backend node2
            \____ Redis 6380 (chi luong redis) ____/
```

## Cau truc repo
```
yarp-lb-lab/
|-- src/
|   |-- LoadBalancer/                # YARP reverse proxy (dung chung, generic)
|   |-- Backend.InMemory/            # backend demo: session in-memory tung node
|   |-- Backend.Redis/               # backend demo: session + DataProtection tren Redis
|   |-- Backend.SimpleLogin.InMemory/# demo LOGIN: key rieng tung node -> mat login khi doi node
|   |-- Backend.SimpleLogin.Redis/   # demo LOGIN: key dung chung Redis -> giu login khi doi node
|   |-- Backend.SignalR.InMemory/    # demo SIGNALR: khong backplane -> tab khac node khong thay tin
|   |-- Backend.SignalR.Redis/       # demo SIGNALR: co Redis backplane -> moi node deu nhan tin
|   |-- Backend.FileUpload.Local/    # demo UPLOAD: luu file local -> doi node bi loi 404
|   |-- Backend.FileUpload.Shared/   # demo UPLOAD: luu file folder chung -> moi node xem duoc
|-- scripts/
|   |-- run-inmemory.sh/.ps1/.bat    # chay bang dotnet (khong IIS)
|   |-- run-redis.sh/.ps1/.bat
|   |-- run-login-inmemory.sh/.ps1/.bat  # demo login (thay loi mat dang nhap)
|   |-- run-login-redis.sh/.ps1/.bat     # demo login (thay giu dang nhap)
|   |-- run-signalr-inmemory.sh/.ps1/.bat# demo SignalR khong backplane
|   |-- run-signalr-redis.sh/.ps1/.bat   # demo SignalR co Redis backplane
|   |-- run-upload-local.sh/.ps1/.bat    # demo Upload luu dia local
|   |-- run-upload-shared.sh/.ps1/.bat   # demo Upload thu muc dung chung
|   |-- run-external.ps1/.bat        # chay app ngoai voi YARP LB (dotnet CLI, tich hop AppDeploy)
|   |-- lib-appdeploy.ps1            # cau noi tu dong nhan dien app va trigger publish tu AppDeploy
|   |-- pick-app.ps1                 # giao dien tuong tac chon app cho iis-setup.bat
|   |-- redis.ps1/.bat               # start/stop/status/view/flush Redis (Docker + native)
|   |-- publish.ps1                  # publish TAT CA 9 project ra ./publish
|   |-- iis-run.ps1                  # wrapper: self-elevate UAC + giu cua so + ghi log
|   |-- setup-iis-local.ps1          # IIS local (YARP LB, self-signed HTTPS)
|   |-- setup-iis-server.ps1         # IIS server all-in-one (scenario 1, ho tro AppPath)
|   |-- setup-nodes.ps1              # CHI node1+node2 (scenario 3, chay tren server)
|   |-- setup-lb.ps1                 # CHI LB, tro toi node remote (scenario 3)
|   |-- setup-iis-arr.ps1            # LB bang IIS ARR (de so sanh)
|   |-- cleanup-iis.ps1              # xoa site/pool/farm
|   |-- iis-setup.bat                # MENU .bat goi cac .ps1 qua iis-run.ps1
|-- docs/
|   |-- architecture.md
|   |-- load-balancer-modes.md
|   |-- backend-state-redis.md
|   |-- simple-login.md              # thi nghiem login qua LB
|   |-- signalr.md                   # thi nghiem realtime chat qua LB
|   |-- file-upload.md               # thi nghiem file upload qua LB
|   |-- iis-local.md
|   |-- iis-server.md
|   |-- iis-split-topology.md        # scenario 1 (1 server) vs 3 (node server / LB local)
|   |-- iis-arr.md
|-- docker-compose.yml               # Redis (map 6380) cho luong redis
|-- YarpLbLab.slnx                   # 9 project
|-- .gitignore / .editorconfig / LICENSE
```

## Yeu cau
- .NET 10 SDK (chay `dotnet`), hoac **.NET 10 Hosting Bundle** (chay tren IIS)
- Docker (tuy chon, cho Redis) hoac Redis tu cai
- Lan build dau can mang de restore NuGet

## A) Chay nhanh bang dotnet (khong IIS)
```bash
./scripts/run-inmemory.sh        # Windows: .\scripts\run-inmemory.ps1 (hoac run-inmemory.bat)
./scripts/run-redis.sh           # Windows: .\scripts\run-redis.ps1 (hoac run-redis.bat)
```
Mo http://localhost:5266 de xem app, hoac mo **http://localhost:5266/dashboard** de xem **YARP Control Center Dashboard** (so do live topology, doi thuat toan RoundRobin/LeastRequests, ban tai thu nghiem va gia lap loi node). Ctrl+C dung LB **va** ca 2 backend.

## A2) Demo LOGIN qua LB (con dang nhap khi doi node khong?)
```bash
./scripts/run-login-inmemory.sh  # Windows: .\scripts\run-login-inmemory.ps1 (hoac .bat)
./scripts/run-login-redis.sh     # Windows: .\scripts\run-login-redis.ps1 (hoac .bat)
```
Mo http://localhost:5266, bam **Login** (user bat ky / password **1234**), xem trang tu refresh
nhay giua node1/node2. Chi tiet + giai thich: [docs/simple-login.md](docs/simple-login.md).
Ctrl+C dung LB **va** ca 2 backend (Windows/Linux).

## A3) Demo REALTIME CHAT (SignalR) qua LB (co nhan duoc tin khi o khac node khong?)
```bash
./scripts/run-signalr-inmemory.sh  # Windows: .\scripts\run-signalr-inmemory.ps1 (hoac .bat)
./scripts/run-signalr-redis.sh     # Windows: .\scripts\run-signalr-redis.ps1 (hoac .bat)
```
Mo 2 trinh duyet (1 cai vao node1, 1 cai vao node2) tai http://localhost:5266 de test chat realtime.
Chi tiet + giai thich: [docs/signalr.md](docs/signalr.md).

## A4) Demo FILE UPLOAD qua LB (o dia local vs Shared folder)
```bash
./scripts/run-upload-local.sh   # Windows: .\scripts\run-upload-local.ps1 (hoac .bat)
./scripts/run-upload-shared.sh  # Windows: .\scripts\run-upload-shared.ps1 (hoac .bat)
```
Mo http://localhost:5266, chon file anh de upload va quan sat loi 404 khi doi node.
Chi tiet + giai thich: [docs/file-upload.md](docs/file-upload.md).

## A5) Chay APP NGOAI (External App) voi Load Balancer (khong can IIS)
```powershell
.\scripts\run-external.ps1       # Hoac chay .\scripts\run-external.bat
```
Nhap duong dan thu muc publish cua ung dung that (vd `D:\Publish\MyApp`), script tu dong clone thanh 2 node va chay YARP LB.

## B) Chay tren IIS
Chay `scripts\iis-setup.bat` (Run as Administrator). Menu:
```
   1 -> Publish TAT CA 9 project
   2 -> Setup IIS LOCAL   (YARP LB + self-signed HTTPS)
   3 -> Setup IIS SERVER  (all-in-one: LB+node1+node2)  [scenario 1]
   4 -> Setup IIS ARR     (LB bang IIS ARR de so sanh)
   5 -> Cleanup
   6 -> Doi backend variant (redis / inmemory / login-redis / login-inmemory / signalr-redis / signalr-inmemory / upload-shared / upload-local)
   7 -> Setup NODES only  (chay TREN SERVER, mo 5001/5002)  [scenario 3]
   8 -> Setup LB only     (chay TREN MAY LB, tro toi IP server)  [scenario 3]
   9 -> Cleanup logs
   A -> AppDeploy Toolkit Hub
   E -> Run External App  (chay app ngoai voi dotnet CLI, khong can IIS)
```
**Thu tu (1 may):** `1` (publish) -> `6` (chon backend) -> `2` hoac `3`.
**Thu tu (split — node o server, LB o may local):** publish tren tung may -> `6` ->
`7` tren server -> `8` tren may local (nhap IP server, vd `199.30.30.91`).
Backend mac dinh la `redis`. Menu 2/3/7 dung backend variant dang chon.

Moi buoc chay qua `iis-run.ps1`: tu xin quyen Admin (UAC), **giu cua so mo**, in loi ro rang
va **ghi log** ra `scripts\iis-setup-<ngay-gio>.log`. Neu co loi, cua so KHONG tu tat.

Chi tiet + cach thu cong:
- [IIS local](docs/iis-local.md)
- [IIS server (scenario 1)](docs/iis-server.md)
- [IIS split topology (scenario 1 & 3)](docs/iis-split-topology.md)
- [IIS ARR (so sanh)](docs/iis-arr.md)

Cac site tao ra: `yarp-lb` (5266), `node1` (5001), `node2` (5002). Backend deu la
App Pool **No Managed Code**, env `NODE_NAME` + `ASPNETCORE_ENVIRONMENT` set theo tung pool.
Menu 8 nap dia chi node qua env var YARP (ghi de `appsettings.json`, khong can publish lai LB).

## Redis (theo connection string cua ban)
- **Local** (`appsettings.json`, env Development):
  `127.0.0.1:6380,abortConnect=false,ssl=false,password=,syncTimeout=5000`
- **Server** (`appsettings.Production.json`, env Production):
  `199.30.30.91:6380,abortConnect=false,ssl=false,password=,syncTimeout=5000`
`docker-compose.yml` map Redis ra cong **6380** cho khop local. Cac script `run-redis*`
goi `scripts/redis.ps1 start` (Docker uu tien, fallback Redis native Windows).

Cong cu Redis (menu / CLI):
```powershell
.\scripts\redis.bat              # menu
.\scripts\redis.ps1 start        # bat Redis
.\scripts\redis.ps1 status
.\scripts\redis.ps1 view         # xem key lab (LbDemo*, SimpleLogin*)
.\scripts\redis.ps1 flush -Force # xoa DB 0
.\scripts\redis.ps1 stop
```

## Cach quan sat
- Man hinh doi mau **node1 (xanh duong) <-> node2 (xanh la)** = round-robin.
- `curl -i http://localhost:5266/` -> header `X-Served-By`.
- Redis: "Session tao luc" GIU NGUYEN khi doi node (in-memory thi bi lech).
- So giay auto-refresh trang: sua `Ui:RefreshSeconds` trong appsettings backend (0 = tat).

## So sanh backend
| Variant | Dac diem | Co che dong bo | Ket qua khi LB doi node | Can Redis |
|---|---|---|---|---|
| **InMemory** | Session trong RAM | Khong chia se | Lech session | Khong |
| **Redis** | Session + DP Keys | Redis Cache + Redis DP | Giu nguyen session | Co |
| **SimpleLogin.InMemory** | Cookie Auth | Key ring rieng tung node | **MAT dang nhap** | Khong |
| **SimpleLogin.Redis** | Cookie Auth | Shared Key ring tren Redis | **GIU dang nhap** | Co |
| **SignalR.InMemory** | Realtime Web | SignalR RAM hub | **MAT tin** giua cac tab | Khong |
| **SignalR.Redis** | Realtime Web | Redis Backplane (Pub/Sub) | **DONG BO tin** moi node | Co |
| **FileUpload.Local** | File Storage | O dia cuc bo tung node | **Loi 404** anh khong tim thay | Khong |
| **FileUpload.Shared** | File Storage | Thu muc chung / Shared Storage | **Hien thi day du** ca 2 node | Khong |

## Chon luong LB (Stateless / Affinity)
Sua `src/LoadBalancer/appsettings.json` -> `"LoadBalancer": { "Mode": "Affinity" }` (hot-reload).
File appsettings cua LB da co comment giai thich tung muc. Chi tiet: [docs/load-balancer-modes.md](docs/load-balancer-modes.md).

## Tai lieu
- [Kien truc](docs/architecture.md)
- [Hai luong load balancing](docs/load-balancer-modes.md)
- [Share state phia backend (Redis)](docs/backend-state-redis.md)
- [Thi nghiem login qua LB](docs/simple-login.md)
- [Thi nghiem realtime SignalR qua LB](docs/signalr.md)
- [Thi nghiem file upload qua LB](docs/file-upload.md)
- [IIS local](docs/iis-local.md) | [IIS server](docs/iis-server.md) | [IIS split topology](docs/iis-split-topology.md) | [IIS ARR](docs/iis-arr.md)

## License
MIT - xem [LICENSE](LICENSE).
