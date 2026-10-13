# Thi nghiem: Realtime Chat qua Load Balancer (SignalR)

Muc tieu: kiem chung cau hoi "khi co nhieu backend node, tin nhan SignalR tu client nay co den duoc client khac o node khac khong?".

## 1. Van de khi scale SignalR qua nhieu node
SignalR su dung ket noi ben bi (persistent connection) nhu WebSocket, Server-Sent Events, hoac Long Polling.
- Khi khong co **Backplane**: Moi server duy tri danh sach ket noi rieng trong RAM cua chinh no.
- Gia su:
  - User A ket noi toi `node1` (cong 5001)
  - User B ket noi toi `node2` (cong 5002)
- Khi User A gui tin nhan broadcast `Clients.All.SendAsync(...)`:
  - `node1` chi biet cac client dang ket noi toi chinh no.
  - User B o `node2` **hoan toan khong nhan duoc** tin nhan!

## 2. Giai phap: Redis Backplane
Redis Backplane dong vai tro la 1 message bus Pub/Sub o giua:
1. User A gui tin toi `node1`.
2. `node1` publish message vao Redis Channel (mac dinh hoac chi dinh prefix `SignalRLbDemo`).
3. Tat ca cac node khac (`node2`, `node3`,...) dang subscribe channel nay se nhan duoc message.
4. `node2` broadcast message xuong cho User B qua WebSocket connection cua no.

```
 [Client A]                     [Client B]
     │                              ▲
     ▼                              │
 [node1]                        [node2]
     │                              ▲
     └──► [Redis Pub/Sub Channel] ──┘
```

## 3. Hai project doi chung

| Project | SignalR Configuration | Ket qua khi mo 2 browser o 2 node |
|---|---|---|
| `Backend.SignalR.InMemory` | `builder.Services.AddSignalR()` (Khong backplane) | **MAT TIN**: Tab o node1 chat thi tab o node2 KHONG thay |
| `Backend.SignalR.Redis` | `.AddSignalR().AddStackExchangeRedis(redisConn)` | **DONG BO**: Tab o node1 chat thi tab o node2 NHAN DUOC NGAY |

## 4. Cach chay

### Ban IN-MEMORY (thay loi khong nhan tin)
```bash
./scripts/run-signalr-inmemory.sh   # Windows: .\scripts\run-signalr-inmemory.ps1 (hoac .bat)
```

### Ban REDIS (thay dong bo realtime)
1. Bat Redis local o cong 6380 (Docker hoac native).
2. Chay:
```bash
./scripts/run-signalr-redis.sh      # Windows: .\scripts\run-signalr-redis.ps1 (hoac .bat)
```

### Tren IIS
Chay `scripts\iis-setup.bat` (Run as Administrator):
- `1` (Publish) -> `6` (Chon backend `e` - signalr-redis hoac `f` - signalr-inmemory) -> `2` (Local) hoac `3` (Server).

## 5. Kich ban test
1. Mo 2 trinh duyet khac nhau (vd Chrome va Edge hoac 1 tab thuong + 1 tab an danh):
   - Browser 1 truy cap http://localhost:5266 (vi du roi vao `node1` mau xanh duong).
   - Browser 2 truy cap http://localhost:5266 (vi du roi vao `node2` mau xanh la).
2. O Browser 1 (node1), go tin nhan va bam **Gui**:
   - **InMemory**: Chi Browser 1 hien thi tin nhan. Browser 2 o node2 khong he co phan hoi.
   - **Redis**: Ca Browser 1 va Browser 2 deu hien thi tin nhan lap tuc, co hien thi ro tag `[node1]` cua nguoi gui.

## 6. Luu y ve Load Balancer voi SignalR
- Khi dung **WebSocket** (uu tien cua SignalR tren trinh duyet hien dai), sau khi bat tay HTTP ban dau ket noi se nang cap thanh TCP full-duplex va giu chat voi 1 node cho den khi dong tab.
- Tuy nhien neu client buoc phai fallback xuong **HTTP Long Polling** (khi mang/proxy chan WebSocket), moi lan gui request co the bi LB day sang node khac. Luc do Load Balancer **bat buoc phai bat Session Affinity (Sticky Session)** de dam bao cac polling request luon toi cung 1 node, neu khong se bi loi `HTTP 404 No Connection with that ID`.
