# Hai luong load balancing

Chon bang setting trong `src/LoadBalancer/appsettings.json`:
```json
"LoadBalancer": { "Mode": "Stateless" }   // hoac "Affinity"
```
Doi + luu -> YARP hot-reload (khong can restart). Override bang env: `LoadBalancer__Mode=Affinity`.

## Stateless (KHUYEN NGHI)
- RoundRobin thuan, KHONG session affinity.
- Yeu cau backend share state (Session + DataProtection) qua Redis -> xem backend-state-redis.md.
- Uu: scale tot, node chet khong mat session, tai deu.

## Affinity
- RoundRobin + sticky cookie (`Yarp.Affinity`), FailurePolicy = Redistribute.
- Moi user dinh 1 node -> session/antiforgery cua node do van dung.
- Nhuoc: node chet -> user tren node do phai login lai; tai co the lech.
- Van nen share DataProtection de failover muot.

## Co che ky thuat
- `LoadBalancerModeConfigFilter` (IProxyConfigFilter) doc `LoadBalancer:Mode`
  va set `LoadBalancingPolicy` + `SessionAffinity` cho tung cluster.
- 1 setting duy nhat dieu khien ca 2 luong, khong can 2 file config.

## Health check
- Active (mac dinh BAT, Path=/health): loai node khong tra 200 khoi vong xoay.
  App generic khong co /health -> tat Active, chi dung Passive.
- Passive (BAT): tu loai node khi request that bai.

## Xem node nao phuc vu
- Header `X-Served-By` tren moi response: `curl -i http://localhost:5266/`
- Log LB: `GET / -> node2`
