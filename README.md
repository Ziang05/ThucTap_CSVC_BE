# CSVC Backend

Backend cho hệ thống quản lý cơ sở vật chất và thiết bị, đã có API cho tài khoản và người dùng, xác thực, phòng ban, khu vực, danh mục thiết bị, nhà cung cấp và nhà sản xuất.

Sử dụng **.NET 10, ASP.NET Core Web API, EF Core, PostgreSQL 17, Swagger và Docker**. Đã có nền tảng DB/repository/migration và API cho tài khoản/người dùng, xác thực bằng phiên DB, phòng ban, cây khu vực, danh mục thiết bị, nhà cung cấp và nhà sản xuất (tạo/sửa/khóa, tra cứu và audit giao dịch). Chi tiết: [tài khoản và email](docs/accounts-api.md), [xác thực](docs/authentication-api.md), [phòng ban](docs/departments-api.md), [khu vực](docs/areas-api.md), [danh mục thiết bị](docs/device-categories-api.md), [nhà cung cấp](docs/suppliers-api.md), [nhà sản xuất](docs/manufacturers-api.md). Bootstrap quản trị và nghiệp vụ thiết bị/phiếu triển khai riêng.

## Bổ sung từ phần mua sắm và kho vật tư

Backend quản lý cơ sở vật chất và thiết bị cho QTTB. Đã có API danh mục, tài khoản/xác thực, hồ sơ thiết bị, nhập, cấp phát/thu hồi qua điều chuyển, mượn trả, báo hỏng, bảo trì/sửa chữa và xuất/nhận lại từ cơ sở dịch vụ. [Kho vật tư dụng cụ](docs/material-inventory.md) quản lý số lượng nhiều, nhập–xuất–tồn, vật tư sử dụng và can thiệp trước–sau trên từng thiết bị.

Nghiệp vụ đích được thống nhất tại [Hợp đồng nghiệp vụ PMQTTB](docs/pmqttb-business-contract.md). [Backlog ưu tiên](docs/pmqttb-priority-backlog.md) liệt kê các phần cần sửa hoặc bổ sung. Thiết bị sử dụng quản lý từng mã với số lượng 0/1; [vật tư dụng cụ có kho số lượng nhiều](docs/material-inventory.md), chứng từ, đối chiếu sử dụng và lịch sử can thiệp thiết bị. Dùng mã vạch; giữ mượn trả như chức năng bổ sung. Yêu cầu mới bắt buộc nhập về QTTB trước rồi cấp phát riêng, hoàn tất toàn bộ một lần và không nhận hủy sau phát sinh thực hiện.

Phân công nhân viên và kiểm kê do thành viên khác phụ trách. Phần này xử lý theo quyền vai trò và liên kết chứng từ trực tiếp.

PMMS vẫn dùng [nguồn giả lập](docs/pmms-simulation.md), chưa có kết nối API thật. Phải thông báo chủ project trước khi tạo hoặc chạy mô phỏng. Lịch sử sửa chữa/vật tư gắn trực tiếp với thiết bị và chứng từ nghiệp vụ.

API đọc lịch sử tài khoản, xác thực, phòng ban và khu vực đã tách danh sách gọn/chi tiết, có lọc/sắp xếp/phân trang tại DB: [History API](docs/history-api.md).

API nghiệp vụ thiết bị: [hồ sơ thiết bị](docs/devices-api.md), [nhập kho](docs/device-receipts-api.md), [mượn trả](docs/device-loans-api.md), [điều chuyển](docs/device-transfers-api.md). Quy trình migrate, backup và release: [Device workflows release guide](docs/device-workflows-release.md).


Task 3 đã tách đơn vị đề xuất và QTTB tiếp nhận cho phiếu nhập mới, giữ nghĩa phiếu cũ; thêm phân loại đang giữ/đang sử dụng và ghi nhận QTTB tự dùng. Migration, API và giao diện: [Nhập về QTTB rồi cấp phát riêng](docs/receipt-custody-release.md).

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

Có thể build/test mà không cần cài SDK trên máy bằng Docker:

```powershell
docker compose --profile tools run --rm tooling build CSVC.sln
docker compose --profile tools run --rm tooling test CSVC.sln
```

Các integration test tự tạo/xóa database test riêng trên PostgreSQL của Compose. Sau migration, đăng nhập manager và chọn phòng phụ trách thiết bị tại **Cấu hình** (`/settings`) trước khi thao tác mượn/trả hoặc điều chuyển. Xem [hướng dẫn cấu hình](docs/equipment-settings-api.md).

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

- [API bảo trì, sửa chữa và phiếu xuất](docs/service-orders-and-exports-api.md)
