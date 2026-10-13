# Kien truc

```
                 Client / Browser
                        |
                        v
        http://localhost:5266  (LoadBalancer / YARP)
         - RoundRobin
         - LoadBalancer:Mode (Stateless | Affinity)
         - Health check (Active /health + Passive)
         - Response header X-Served-By
                        |
            +-----------+-----------+
            |                       |
  http://localhost:5001    http://localhost:5002
     Backend node1            Backend node2
            \____ (chi ban Redis) Redis 6380 ____/
                  Session + DataProtection dung chung
```

## Thanh phan
- **src/LoadBalancer**: reverse proxy YARP. Khong biet backend la app gi -> tai su dung cho moi project.
- **src/Backend.InMemory**: backend demo, session in-memory rieng tung node (de thay van de).
- **src/Backend.Redis**: backend demo, session + DataProtection tren Redis (fix van de).
- **src/Backend.SimpleLogin.InMemory**: demo LOGIN, DataProtection key rieng tung node -> mat login khi doi node.
- **src/Backend.SimpleLogin.Redis**: demo LOGIN, DataProtection key dung chung Redis -> giu login khi doi node.
- **src/Backend.SignalR.InMemory**: demo REALTIME CHAT, SignalR khong backplane -> khac node khong nhan duoc tin.
- **src/Backend.SignalR.Redis**: demo REALTIME CHAT, SignalR co Redis Backplane -> moi node deu nhan duoc tin.
- **src/Backend.FileUpload.Local**: demo FILE UPLOAD, luu file local tung node -> doi node bi loi 404.
- **src/Backend.FileUpload.Shared**: demo FILE UPLOAD, luu file vao thu muc dung chung -> ca 2 node deu doc duoc.

## Luong request
1. Client goi LB (5266).
2. Config filter set RoundRobin (+ affinity neu Mode=Affinity).
3. YARP chon node con "healthy", forward toi 5001/5002.
4. Response transform gan header X-Served-By = node da phuc vu.

## Tai su dung cho project khac
Chi doi `Destinations` trong `src/LoadBalancer/appsettings.json` tro toi node1/node2
cua project ban. Khong sua code LB.
Khi deploy split (LB va node khac may), co the ghi de destination bang env var
`ReverseProxy__Clusters__demo__Destinations__node*__Address` (xem
[iis-split-topology.md](iis-split-topology.md) / `setup-lb.ps1`).
