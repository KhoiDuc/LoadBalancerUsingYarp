# Share state phia backend (Redis)

LB chi chia request. "Tinh dung" khi chay nhieu node phu thuoc app share state hay chua.
Day la phan quan trong nhat, thuong bi bo qua.

## 3 thu can share (xem src/Backend.Redis/Program.cs)
```csharp
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

var redis = await ConnectionMultiplexer.ConnectAsync(redisConn);

// 1) DATA PROTECTION dung chung -> fix loi antiforgery 400 / login loop
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redis, "LbDemo-DP-Keys")
    .SetApplicationName("LbDemo"); // PHAI giong het nhau tren moi node

// 2) SESSION / CACHE dung chung
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
builder.Services.AddSession();

// 3) HEALTH endpoint cho LB
builder.Services.AddHealthChecks();
```

## Vi sao bat buoc
Moi ASP.NET Core app co key ring DataProtection rieng. Neu khong share, cookie/
antiforgery token do node A ma hoa thi node B khong giai ma duoc -> loi 400 ngau nhien,
login loop khi round-robin. Share DataProtection + cung SetApplicationName la bat buoc.

## Package (.NET 10)
- Microsoft.AspNetCore.DataProtection.StackExchangeRedis 10.0.9 (>= 10.0.7: CVE-2026-40372)
- Microsoft.Extensions.Caching.StackExchangeRedis 10.0.9
- StackExchange.Redis 3.0.17

## Forwarded headers (nen co)
```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
app.UseForwardedHeaders(); // dat som nhat trong pipeline
```
YARP mac dinh da gui X-Forwarded-* nen khong can them transform.

## Cong cu Redis (local)
Dung `scripts/redis.ps1` (hoac `scripts/redis.bat`) de start/stop/status/view/flush.
Uu tien Docker (`docker-compose.yml`, port **6380**); neu khong co Docker se thu
`redis-server.exe` native (`C:\Program Files\Redis`).

```powershell
.\scripts\redis.ps1 start
.\scripts\redis.ps1 view          # chi key lab: LbDemo*, SimpleLogin*
.\scripts\redis.ps1 view -AllKeys
.\scripts\redis.ps1 flush         # hoi xac nhan roi FLUSHDB 0
.\scripts\redis.ps1 stop
```

Khi chay demo, `run-redis.ps1` / `run-login-redis.ps1` tu goi `redis.ps1 start`.

