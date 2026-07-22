# yarp-lb-lab

Mot lab nho de **hoc va test load balancing** bang [YARP](https://github.com/dotnet/yarp)
tren .NET 10, kem huong dan chay tren **IIS local & IIS server** (co ca ban **IIS ARR** de so sanh).
Mot Load Balancer dung chung + cac backend demo (in-memory & Redis + demo login) de thay ro
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
|-- scripts/
|   |-- run-inmemory.sh/.ps1/.bat    # chay bang dotnet (khong IIS)
|   |-- run-redis.sh/.ps1/.bat
|   |-- run-login-inmemory.sh/.ps1/.bat  # demo login (thay loi mat dang nhap)
|   |-- run-login-redis.sh/.ps1/.bat     # demo login (thay giu dang nhap)
|   |-- redis.ps1/.bat               # start/stop/status/view/flush Redis (Docker + native)
|   |-- publish.ps1                  # publish TAT CA 5 project ra ./publish
|   |-- iis-run.ps1                  # wrapper: self-elevate UAC + giu cua so + ghi log
|   |-- setup-iis-local.ps1          # IIS local (YARP LB, self-signed HTTPS)
|   |-- setup-iis-server.ps1         # IIS server all-in-one (scenario 1)
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
|   |-- iis-local.md
|   |-- iis-server.md
|   |-- iis-split-topology.md        # scenario 1 (1 server) vs 3 (node server / LB local)
|   |-- iis-arr.md
|-- docker-compose.yml               # Redis (map 6380) cho luong redis
|-- YarpLbLab.slnx                   # 5 project
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
Mo http://localhost:5266. Ctrl+C dung LB **va** ca 2 backend.

## A2) Demo LOGIN qua LB (con dang nhap khi doi node khong?)
```bash
./scripts/run-login-inmemory.sh  # Windows: .\scripts\run-login-inmemory.ps1 (hoac .bat)
./scripts/run-login-redis.sh     # Windows: .\scripts\run-login-redis.ps1 (hoac .bat)
```
Mo http://localhost:5266, bam **Login** (user bat ky / password **1234**), xem trang tu refresh
nhay giua node1/node2. Chi tiet + giai thich: [docs/simple-login.md](docs/simple-login.md).
Ctrl+C dung LB **va** ca 2 backend (Windows/Linux).

## B) Chay tren IIS
Chay `scripts\iis-setup.bat` (Run as Administrator). Menu:
```
   1 -> Publish TAT CA 5 project
   2 -> Setup IIS LOCAL   (YARP LB + self-signed HTTPS)
   3 -> Setup IIS SERVER  (all-in-one: LB+node1+node2)  [scenario 1]
   4 -> Setup IIS ARR     (LB bang IIS ARR de so sanh)
   5 -> Cleanup
   6 -> Doi backend variant (redis / inmemory / login-redis / login-inmemory)
   7 -> Setup NODES only  (chay TREN SERVER, mo 5001/5002)  [scenario 3]
   8 -> Setup LB only     (chay TREN MAY LB, tro toi IP server)  [scenario 3]
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
| | InMemory | Redis | SimpleLogin.InMemory | SimpleLogin.Redis |
|---|---|---|---|---|
| Session | in-memory | Redis | - | - |
| DataProtection keys | rieng node | Redis chung | rieng node | Redis chung |
| Login qua node khac | - | - | MAT login | GIU login |
| Can Redis | Khong | Co | Khong | Co |

## Chon luong LB (Stateless / Affinity)
Sua `src/LoadBalancer/appsettings.json` -> `"LoadBalancer": { "Mode": "Affinity" }` (hot-reload).
File appsettings cua LB da co comment giai thich tung muc. Chi tiet: [docs/load-balancer-modes.md](docs/load-balancer-modes.md).

## Tai lieu
- [Kien truc](docs/architecture.md)
- [Hai luong load balancing](docs/load-balancer-modes.md)
- [Share state phia backend (Redis)](docs/backend-state-redis.md)
- [Thi nghiem login qua LB](docs/simple-login.md)
- [IIS local](docs/iis-local.md) | [IIS server](docs/iis-server.md) | [IIS split topology](docs/iis-split-topology.md) | [IIS ARR](docs/iis-arr.md)

## License
MIT - xem [LICENSE](LICENSE).
