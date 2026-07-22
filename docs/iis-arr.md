# LB bang IIS ARR (de SO SANH voi YARP)

IIS co san bo LB rieng: **Application Request Routing (ARR)** + **URL Rewrite**.
Day la lua chon thay cho project YARP. Ca hai cung ket qua (round-robin 2 node),
nhung khac ban chat.

## 1. Cai dat
- Cai **URL Rewrite** va **Application Request Routing 3.0** (qua Web Platform Installer
  hoac tai MSI tu microsoft.com). Sau khi cai, IIS Manager co muc "Application Request Routing Cache"
  va node co "URL Rewrite".
- node1 (5001) & node2 (5002) phai dang chay (chay setup-iis-local.ps1 / setup-iis-server.ps1 truoc).

## 2. Tu dong
Run as Administrator:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-iis-arr.ps1 -Port 8080
```
Script se: bat proxy cap server, tao web farm `backend-farm` (localhost:5001 + 127.0.0.1:5002),
set health check `/health` + thuat toan WeightedRoundRobin, tao site `arr-lb` :8080 voi rewrite
rule route toan bo request vao farm.

> Meo: tren 1 may, ARR khong cho 2 server trung dia chi, nen dung `localhost` cho node1 va
> `127.0.0.1` cho node2 (deu local) de tach nhau.

## 3. Thu cong (IIS Manager)
1. Server node > **Application Request Routing Cache** > Server Proxy Settings > tick **Enable proxy**.
2. Chuot phai **Server Farms** > Create Server Farm `backend-farm` > them 2 server:
   `localhost` (Advanced > httpPort 5001) va `127.0.0.1` (httpPort 5002).
3. Farm > **Health Test**: URL `http://localhost/health`. **Load Balance**: Weighted round robin.
4. Khi tao farm, IIS hoi "tao rewrite rule?" > Yes (no route site mac dinh vao farm),
   hoac tao site rieng `arr-lb` :8080 + URL Rewrite rule action Rewrite ->
   `http://backend-farm/{R:1}`.
5. `iisreset`.

## 4. Kiem tra
```
curl -i http://localhost:8080/
```
Doi node1/node2 giong YARP. So sanh voi YARP LB o :5266.

## 5. YARP vs ARR - chon cai nao?
| | YARP (project nay) | IIS ARR |
|---|---|---|
| Ban chat | thu vien .NET, code C# | tinh nang cua IIS, cau hinh UI/appcmd |
| Tuy bien | rat manh (transform, filter, DI) | gioi han theo UI ARR |
| Trien khai | tu host / sau IIS/nginx | phai co IIS + ARR + URL Rewrite |
| Doi hop | dev muon logic tang app, test | shop da chuan IIS, muon zero-code |
| Health check | Active + Passive (code) | Health Test cua farm |

Khuyen nghi: neu ban da theo huong code (.NET), **giu YARP**. ARR o day chi de
ban thay va so sanh 2 cach.
