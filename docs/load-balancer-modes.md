# Che do Load Balancing & Dashboard dieu khien

## 1. YARP Control Center Dashboard
YARP Gateway tich hop san giao dien giam sat va dieu khien truc quan tai:
👉 **`http://localhost:5266/dashboard`**

Cac tinh nang tren Dashboard:
- **Live Topology**: So do truc quan Client &rarr; YARP Gateway (:5266) &rarr; Node 1 (:5001) & Node 2 (:5002).
- **Trang thai Suc khoe Node**: Healthy/Unhealthy (Active Health Check & Passive Health Check), so luong In-flight Request dang xu ly, Latency trung binh, ti le loi.
- **Dieu khien Thuat toan Dong**: Chuyen doi tuc thi giua `RoundRobin`, `LeastRequests`, `Random`, `PowerOfTwoChoices` ma khong can restart Gateway.
- **Gia lap Su co tu xa (Remote Failure Injection)**:
  - 🟢 **Binh thuong (Normal)**: 0ms delay, HTTP 200.
  - 🟡 **Chay cham (Slow)**: Do tre 5s de kiem chung thuat toan LeastRequests va Passive Health Check.
  - 🔴 **Loi 503 (Error503)**: Tra ve HTTP 503 tren ca `/health` va moi endpoint de kich hoat Active Health Check loai node.
- **May phat tai (Traffic Generator Lab)**:
  - Ban 1 Request, 10 Requests tuan tu, hoac 20 Requests dong thoi (Concurrency).
  - Tong hop ti le phan chia giua cac node va min/avg/max latency.
- **Nhat ky Phan luong Thoi gian thuc (Live Request Stream)**:
  - Bang log tu cap nhat moi 1s ghi nhan ID, Method, Path, Node phuc vu (`node1` / `node2`), Ma HTTP, va Do tre ms.

---

## 2. Cac thuat toan Load Balancing ho tro
1. **RoundRobin (Mac dinh)**: Chia deu luon phien giua cac node (1 &rarr; 2 &rarr; 1 &rarr; 2).
2. **LeastRequests**: Uu tien node dang co it request dang xu ly nhat. Rat hieu qua khi 1 node bi cham.
3. **Random**: Chon ngau nhien destination.
4. **PowerOfTwoChoices**: Chon ngau nhien 2 node, roi lay node co it in-flight request hon.

---

## 3. Che do Session Affinity (Sticky Session)
Chon bang setting trong `src/LoadBalancer/appsettings.json` hoac 1-click tren Dashboard:
```json
"LoadBalancer": {
  "Mode": "Stateless",      // "Stateless" hoac "Affinity"
  "Algorithm": "RoundRobin" // "RoundRobin", "LeastRequests", "Random", "PowerOfTwoChoices"
}
```

### Stateless (KHUYEN NGHI KHI DA DUNG REDIS)
- RoundRobin/LeastRequests thuan, KHONG session affinity.
- Yeu cau backend share state (Session + DataProtection) qua Redis.
- Uu diem: Scale tot, 1 node chet khong mat session, tai deu.

### Affinity (STICKY COOKIE)
- YARP them cookie `Yarp.Affinity`, FailurePolicy = Redistribute.
- Moi user "dinh" vao 1 node -> session/antiforgery tren RAM cua node do van dung duoc.
- Nhuoc diem: Node chet -> user dinh node do phai dang nhap lai; tai co the lech.

---

## 4. Health Check & Failover
- **Active (Interval=5s, Timeout=3s, ConsecutiveFailures=2)**:
  - YARP tu dong ping `/health` dinh ky.
  - Khi node loi 2 lan lien tiep (10s), YARP tu dong loai node khoi vong xoay va chuyen 100% traffic sang node con lai.
- **Passive (TransportFailureRate=50%, ReactivationPeriod=15s)**:
  - YARP quan sat truc tiep cac request that bai.
