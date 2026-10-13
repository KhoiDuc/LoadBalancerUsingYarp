# Thi nghiem: File Upload qua Load Balancer (FileUpload)

Muc tieu: kiem chung cau hoi "khi client upload file qua Load Balancer, tai sao luu file tren o dia local cua server se gay loi 404?".

## 1. Van de khi luu file local tren cluster nhieu node
Khi he thong web chay phia sau Load Balancer:
1. Client gui request `POST /upload` kem file avatar.
2. Load Balancer dieu huong request toi `node1`.
3. `node1` luu file vao o dia cuc bo cua no (`local_uploads/latest.jpg`).
4. Ngay sau do, Client reload hoac trinh duyet goi `GET /uploads/latest.jpg`.
5. Do Load Balancer chia tai (Round-Robin), request `GET` nay duoc chuyen sang `node2`.
6. `node2` kiem tra o dia local cua chinh no -> Khong co file nay -> **Tra ve HTTP 404 Not Found!**

## 2. Giai phap: Shared Storage (Thu muc dung chung hoac Object Storage)
De moi node deu truy cap duoc file:
- **Shared Folder / NAS / SMB**: Tat ca cac node cung mount hoac tro vao 1 thu muc dung chung (`YarpLbLab_SharedUploads`).
- **Object Storage**: Trong cac he thong lon, file duoc day len MinIO, AWS S3, Cloudflare R2 hoac Azure Blob Storage.

```
       [Client POST /upload]              [Client GET /uploads/latest.jpg]
                 │                                       │
                 ▼                                       ▼
             [node1]                                 [node2]
                 │                                       │
                 ▼                                       ▼
        ┌────────────────────────────────────────────────────────┐
        │  Shared Directory / Storage (Doc & Ghi dong thoi)      │
        └────────────────────────────────────────────────────────┘
```

## 3. Hai project doi chung

| Project | Storage | Ket qua khi LB doi node |
|---|---|---|
| `Backend.FileUpload.Local` | O dia cuc bo tung node (`local_uploads`) | **LOI 404**: Node khac khong co file, bao loi do |
| `Backend.FileUpload.Shared` | Thu muc dung chung (`YarpLbLab_SharedUploads`) | **THANH CONG**: Ca 2 node deu doc duoc anh |

## 4. Cach chay

### Ban LOCAL (thay loi 404)
```bash
./scripts/run-upload-local.sh      # Windows: .\scripts\run-upload-local.ps1 (hoac .bat)
```

### Ban SHARED (thay hoat dong on dinh)
```bash
./scripts/run-upload-shared.sh     # Windows: .\scripts\run-upload-shared.ps1 (hoac .bat)
```

### Tren IIS
Chay `scripts\iis-setup.bat` (Run as Administrator):
- `1` (Publish) -> `6` (Chon backend `g` - upload-shared hoac `h` - upload-local) -> `2` (Local) hoac `3` (Server).

## 5. Kich ban test
1. Mo http://localhost:5266 tren trinh duyet.
2. Chon 1 file anh bat ky va bam **Upload & Luu**:
   - Trang se tu reload moi 2 giay, LB lu luan chuyen giua `node1` (mau xanh duong) va `node2` (mau xanh la).
   - **Local Storage**: 
     - Khi trang o dung node vua nhan upload: Anh hien thi binh thuong.
     - Khi trang nhay sang node kia: Khung hien thi bao loi mau do: `❌ KHONG TIM THAY FILE TREN NODE! File luu o node khac nen node nay khong doc duoc`.
   - **Shared Storage**:
     - Bất kể trang nhay sang node1 hay node2, anh avatar van luon hien thi day du va on dinh.
