# Thi nghiem: Login qua Load Balancer (SimpleLogin)

Muc tieu: kiem chung cau hoi "sau khi login xong, request roi vao node khac thi con dang nhap khong?".

Cau tra loi phu thuoc vao **ASP.NET Core Data Protection** - thanh phan ma hoa/giai ma **cookie authentication**.
Neu moi node co key ring rieng thi cookie tao o node1 se khong giai ma duoc o node2.

## 2 phien ban

| Project | Data Protection | Ket qua ky vong khi LB doi node |
|---|---|---|
| `Backend.SimpleLogin.InMemory` | Key **rieng tung node** (`SetApplicationName("SimpleLogin-<node>")` + thu muc key rieng) | **MAT dang nhap** o node chua login (trang do: "CHUA dang nhap") |
| `Backend.SimpleLogin.Redis` | Key **dung chung** qua Redis (`PersistKeysToStackExchangeRedis` + `SetApplicationName("SimpleLogin")`) | **VAN dang nhap** o moi node (trang xanh: "DANG dang nhap") |

Ca hai deu dung round-robin, KHONG session affinity, de moi lan refresh se luan phien node1/node2.

## Cach chay

### Ban IN-MEMORY (thay loi)
```bash
./scripts/run-login-inmemory.sh   # Windows: .\scripts\run-login-inmemory.ps1 (hoac .bat)
```

### Ban REDIS (thay dung)
1. Bat Redis local o cong 6380 (vd `redis-server --port 6380`) hoac de script tu chay Docker.
2. Chay:
```bash
./scripts/run-login-redis.sh      # Windows: .\scripts\run-login-redis.ps1 (hoac .bat)
```

### Tren IIS
Menu `iis-setup.bat`: `1` (publish) -> `6` chon `login-inmemory` hoac `login-redis` -> `2`/`3`.

## Kich ban test

1. Mo http://localhost:5266
2. Bam **Login** — form `/login` cung tu refresh (nhay `node1`/`node2`, border doi mau); username/password van giu.
3. Dang nhap: username bat ky, password **1234** (hoac `admin`/`admin`).
4. Sau khi login, trang `/` tu refresh moi 2 giay -> LB luan phien node1/node2.
   - **IN-MEMORY**: trang nhay qua lai giua **xanh (DANG dang nhap)** va **do (CHUA dang nhap)** -> chung minh cookie chi doc duoc o dung node da phat hanh.
   - **REDIS**: trang **luon xanh** o ca 2 node -> cookie portable nho key dung chung.
5. Xem header `X-Served-By` (do LB them) de biet node nao dang phuc vu.

## Vi sao?

- Cookie authentication luu principal trong 1 cookie da duoc **Data Protection ma hoa**.
- Node chi giai ma duoc cookie neu co **cung key ring** va **cung application name (purpose)**.
- Tren nhieu server that, moi server tu sinh key ring rieng -> phai **chia se key** (Redis, file share, Azure Key Vault, ...) + **dat cung `SetApplicationName`** thi SSO giua cac node moi hoat dong.
- Day cung la ly do vi sao production nen: hoac chia se Data Protection keys, hoac bat **session affinity** (kem tối ưu hon).

## Ghi chu

- Ban InMemory luu key ring rieng tung node trong thu muc TEMP: `%TEMP%\SimpleLogin-dp-keys-<node>`
  (dung TEMP de app pool IIS ghi duoc; khi chay dotnet thi la TEMP cua user). Day la key ring rieng
  cua tung node - chinh la thu gay ra "mat dang nhap" khi doi node (co tinh minh hoa).
- Demo bo qua antiforgery cho gon; app that nen bat lai (antiforgery cung phu thuoc Data Protection).
