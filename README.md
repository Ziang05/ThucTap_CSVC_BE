# CSVC Backend

Backend cho hệ thống quản lý cơ sở vật chất và thiết bị, ưu tiên 4 nhóm chức năng: tài khoản và người dùng, xác thực, phòng ban và khu vực.

Sử dụng **.NET 10, ASP.NET Core Web API, EF Core, PostgreSQL 17, Swagger và Docker**. Đã có nền tảng DB/repository/migration và API cho bốn nhóm: tài khoản/người dùng, xác thực bằng phiên DB, phòng ban và cây khu vực (tạo/sửa/chuyển nhánh/khóa, danh mục, audit giao dịch). Chi tiết: [tài khoản và email](docs/accounts-api.md), [xác thực](docs/authentication-api.md), [phòng ban](docs/departments-api.md), [khu vực](docs/areas-api.md). Bootstrap quản trị và nghiệp vụ thiết bị/phiếu triển khai riêng.

API đọc lịch sử tài khoản, xác thực, phòng ban và khu vực đã tách danh sách gọn/chi tiết, có lọc/sắp xếp/phân trang tại DB: [History API](docs/history-api.md).

## Chạy bằng Docker

Cần Docker Desktop đang chạy ở chế độ Linux containers. Thực hiện các lệnh tại thư mục gốc project:

```powershell
# Tạo cấu hình ở lần chạy đầu.
if (-not (Test-Path .env)) { Copy-Item .env.example .env }

docker compose build api
docker compose run --rm api --migrate
docker compose up -d
```

Mở Swagger tại **http://localhost:8080/swagger**.

Có thể chỉnh database, mật khẩu và cổng trong `.env`. Swagger chỉ bật ở môi trường `Development`, đã được đặt trong `.env.example`.

Xem log và dừng ứng dụng:

```powershell
docker compose logs -f api
docker compose down
```

## Chạy API local

Cần **SDK .NET 10** và PostgreSQL. Có thể dùng Docker để chạy riêng PostgreSQL:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
docker compose up -d postgres

dotnet run --project src/CSVC.API --launch-profile http -- --migrate
dotnet run --project src/CSVC.API --launch-profile http
```

Chuỗi kết nối trên dùng cấu hình mẫu; điều chỉnh cho khớp với PostgreSQL của bạn. Nếu API Docker đang dùng cổng 8080, chạy `docker compose stop api` trước khi chạy API local.

Swagger vẫn tại **http://localhost:8080/swagger**. Nhấn `Ctrl+C` để dừng API.

Quy ước cho team: [CONTRIBUTING.md](CONTRIBUTING.md). Chi tiết kiến trúc: [docs/architecture.md](docs/architecture.md).
