# Setup tren IIS LOCAL

Host 3 app tren IIS may dev: `yarp-lb` (5266), `node1` (5001), `node2` (5002). YARP lam LB.

## 1. Cai dat truoc
- **.NET 10 Hosting Bundle** (runtime + ASP.NET Core Module V2). Sau khi cai: `iisreset`.
- Bat IIS: Control Panel > Programs > Turn Windows features on/off > Internet Information Services.
- Redis local o `127.0.0.1:6380` (theo connection string trong appsettings.json).
  Cach nhanh: `docker compose up -d` (compose da map 6380).

## 2. Publish
```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```
Ra thu muc `publish\lb`, `publish\backend-redis`, `publish\backend-inmemory`.

## 3. Setup IIS (tu dong)
Chay bang quyen Administrator:
```powershell
# Cach 1: dung menu bat (de nhat)
scripts\iis-setup.bat   -> chon 1 (Publish) roi 2 (Setup LOCAL)

# Cach 2: goi truc tiep
powershell -ExecutionPolicy Bypass -File scripts\setup-iis-local.ps1 -Https
```
Script se: tao 3 App Pool (No Managed Code) + 3 Site + binding, set env `NODE_NAME` va
`ASPNETCORE_ENVIRONMENT=Development` cho tung pool, tao self-signed cert (neu -Https).

Tham so:
- `-BackendVariant redis|inmemory` (mac dinh redis)
- `-Https` + `-HttpsPort 5443`

## 4. Setup IIS (thu cong qua IIS Manager)
Lap lai cho moi app (node1/node2/lb):
1. Application Pools > Add > .NET CLR version = **No Managed Code**.
2. Sites > Add Website: chon thu muc publish tuong ung, App Pool vua tao, Port (5001/5002/5266).
3. node1/node2 tro cung `publish\backend-redis`; set env khac nhau:
   Configuration Editor > `system.applicationHost/applicationPools` > pool > environmentVariables:
   `NODE_NAME=node1` (hoac node2), `ASPNETCORE_ENVIRONMENT=Development`.
4. `iisreset`.

## 5. Kiem tra
- Mo http://localhost:5266 (tu refresh 2s, doi node1 <-> node2).
- `curl -i http://localhost:5266/` -> header `X-Served-By`.
- Redis: "Session tao luc" GIU NGUYEN khi doi node.

## 6. Loi hay gap
| Loi | Nguyen nhan | Cach xu ly |
|---|---|---|
| HTTP 500.19 | thieu ANCM / web.config sai | cai lai Hosting Bundle |
| HTTP 500.30 | app khong start (vd Redis down) | bat stdout log, kiem tra Redis 6380 |
| HTTP 502.5 | sai hosting model / runtime | kiem tra .NET 10 runtime + publish dung |
| Doi node ma session nhay | dung backend-inmemory | doi sang backend-redis |

Bat log tam de debug: sua `web.config` trong thu muc publish, dat
`stdoutLogEnabled="true"` va tao thu muc `logs`. Nho tat lai sau khi xong.
