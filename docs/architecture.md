# Kiến trúc base và bối cảnh nghiệp vụ

## Nguồn tham khảo và phạm vi

Đã đọc `Documents/Danh sách nghiệp vụ.xlsx` (danh sách tổng và 4 sheet đặc tả), `Documents/qltb_4nhom.dbml`, xem phần mô tả tổng quan trong 4 SRS cũ, và tham khảo cấu trúc `D:/Git_Project/Do_An_1/src`. Workbook và DBML là cơ sở cho 4 nhóm được ưu tiên. Tài liệu SRS, activity và sequence cũ chỉ cung cấp bối cảnh hệ thống thiết bị; không dùng để ghi đè 4 nhóm đã chỉnh sửa.

Base đã có hạ tầng, 12 entity, repository riêng và migration `InitialFourModules` hoàn thiện các ràng buộc DB cho 4 nhóm. Đã triển khai API/use case quản lý tài khoản và người dùng, AutoMapper, audit giao dịch, quên mật khẩu qua SMTP và kiểm tra phiên DB; xem [accounts-api.md](accounts-api.md). Nhóm 2 đã có đăng nhập/cấp phiên, đăng xuất, throttle theo email và expiration worker; xem [authentication-api.md](authentication-api.md). Nhóm 3 có DTO/AutoMapper/service/controller phòng ban và audit giao dịch; xem [departments-api.md](departments-api.md). Chưa có tài khoản mặc định hoặc bootstrap. Khu vực, thiết bị và xử lý phiếu triển khai riêng; các ràng buộc DB được liệt kê trong phần schema. Tài liệu gốc ở `Documents/` được Git ignore theo yêu cầu; ghi chú phát triển được giữ trong `docs/`.

## Tổ chức solution

Giữ tên 4 tầng quen thuộc từ project tham khảo:

| Project | Trách nhiệm | Phụ thuộc |
| --- | --- | --- |
| `CSVC.Core` | Entity, value object, quy tắc domain, `DomainException` | Thư viện chuẩn .NET |
| `CSVC.UseCases` | Điều phối nghiệp vụ, DTO, interface persistence và dịch vụ ngoài | Core, DI abstractions, AutoMapper |
| `CSVC.Infrastructure` | EF Core, PostgreSQL, repository, email và dịch vụ ngoài khi cần | Core, UseCases |
| `CSVC.API` | HTTP, controller, DI composition, middleware, health và Swagger | UseCases, Infrastructure |

Điều chỉnh so với project tham khảo: `UseCases` không tham chiếu `Infrastructure`; đăng ký DI chia theo tầng; cấu hình DB không chứa credentials trong Git; không chạy migration hoặc tạo admin mặc định lúc API khởi động. AutoMapper 16.2.0 được dùng cho DTO tài khoản và SQL projection; chưa thêm Redis, message broker hoặc MediatR.

Khi thêm nhóm nghiệp vụ, tổ chức code theo nhóm trong mỗi tầng, ví dụ:

```text
Core/Accounts/Account.cs
UseCases/Accounts/CreateAccount/CreateAccountRequest.cs
UseCases/Accounts/CreateAccount/CreateAccountService.cs
UseCases/Abstractions/Persistence/IAccountRepository.cs
Infrastructure/Persistence/Configurations/AccountConfiguration.cs
Infrastructure/Persistence/Repositories/AccountRepository.cs
API/Controllers/AccountsController.cs
```

Repository và Unit of Work giữ pattern từ project tham khảo, với các hợp đồng được đặt trong `UseCases/Abstractions/Persistence`: `IRepository<T>`, `IUnitOfWork` và 12 interface repository riêng. Interface riêng kế thừa `IRepository<TEntity>`; implementation kế thừa `GenericRepository<TEntity>`. Đây là các khả năng mà use case cần để truy cập dữ liệu; Core chứa entity và quy tắc nghiệp vụ.

Infrastructure triển khai các hợp đồng trên, gom thành phần lưu trữ dưới `Persistence`: `CsvcDbContext`, `Repositories/`, `Configurations/`, `Migrations/` và `UnitOfWork.cs`. `CsvcDbContext` chỉ phụ trách EF, không triển khai interface Unit of Work. Controller không truy vấn DbContext.

DI đăng ký scoped từng repository riêng, `IRepository<> → GenericRepository<>` cho trường hợp cần dùng trực tiếp và `IUnitOfWork → UnitOfWork`; tất cả dùng chung scoped `CsvcDbContext`. Unit of Work nhận các interface riêng qua constructor và cung cấp `Accounts`, `UserProfiles`, `Roles`, `AccountHistories`, `PasswordResetRequests`, `AuthSessions`, `AuthLoginThrottles`, `AuthHistories`, `Departments`, `DepartmentHistories`, `Areas`, `AreaHistories`. Repository inject trực tiếp và property tương ứng là cùng instance trong scope. Đã bỏ factory/cache `Repository<T>()` khỏi Unit of Work.

Repository chỉ stage thay đổi; `CommitAsync` gọi `SaveChangesAsync` để lưu chung dữ liệu và audit. DI sở hữu vòng đời context, Unit of Work không dispose context riêng.

Đã có truy vấn riêng theo email account đã chuẩn hóa, mã phòng ban, node khu vực con trực tiếp và phiên active chưa hết hạn. Những truy vấn sâu hơn sẽ bổ sung theo use case. Truy vấn danh sách/đọc dùng `AsNoTracking`; tìm theo khóa và `FirstOrDefaultTrackedAsync` dùng tracking để sửa. Interface generic có thao tác xóa vật lý, nhưng use case account/phòng ban/khu vực/audit không được dùng để bỏ qua quy định bảo toàn lịch sử.

Module tài khoản dùng `IAccountTransaction` với transaction PostgreSQL và advisory lock để bảo vệ đọc–kiểm tra–ghi, quy tắc quản trị cuối cùng và tiêu thụ mã reset một lần. Các writer account/session/reset bổ sung sau phải phối hợp cùng lock; chỉ `CommitAsync` không đủ chống cuộc đua nghiệp vụ. Test EF InMemory xác nhận repository, DI và commit/tracking; test PostgreSQL tùy chọn tạo DB riêng để xác nhận kiểu native và rollback khi audit bị vi phạm unique index. Không dùng DB ứng dụng để tạo/xóa bảng phục vụ test.

Core chưa có `BaseEntity` chứa `CreatedAt`, `UpdatedAt`, `DeletedAt`: DBML yêu cầu suy ra thời điểm tạo từ lịch sử và không dùng xóa mềm thay cho trạng thái khóa. Khi thêm entity, chọn thuộc tính theo mô hình đã thống nhất.

## Nền tảng kỹ thuật

- .NET 10 LTS, EF Core 10, Npgsql EF provider 10, PostgreSQL 17 và Swashbuckle 10.
- `CsvcDbContext` có 12 DbSet và tự tìm các `IEntityTypeConfiguration<T>` trong Infrastructure. `InitialFourModules` tạo 12 bảng với tên snake_case, native enum nhãn viết hoa, UUID, JSONB, INET, DATE, TIMESTAMPTZ, foreign key Restrict và các ràng buộc bổ sung bên dưới. SaveChanges sync/async chuẩn hóa email, tên phòng ban/khu vực và khóa email throttle trước khi lưu.
- `TimeProvider.System` được đăng ký qua DI để use case dùng thời gian UTC và có thể thay bằng clock kiểm soát trong test.
- API sử dụng JSON camelCase, enum dạng chuỗi, HTTP status đúng loại lỗi và `ProblemDetails`.
- `DomainException`/`UseCaseException` chỉ nhận code và message an toàn để trả client. Lỗi concurrency của EF trả 409; lỗi ngoài dự kiến trả 500 với nội dung chung.
- Log console dạng JSON, UTC, có scope RequestId; không log toàn bộ request/response. Audit tài khoản được ghi tường minh từ use case, không suy diễn từ HTTP log hay interceptor ghi mọi thuộc tính.
- Mỗi HTTP request nhận UUID mới do server tạo. Idempotency khi triển khai nghiệp vụ cần hợp đồng riêng, kiểm tra phạm vi account/action/payload và kết quả đã lưu; không tin header client để bỏ qua thao tác hoặc quyền truy cập.
- `/health/live` chỉ phản ánh process; `/health/ready` kiểm tra kết nối PostgreSQL và trả 503 khi không sẵn sàng. Dùng lệnh EF/migration task để kiểm tra và cập nhật phiên bản schema; readiness hiện không kiểm tra pending migration.
- Không dùng EF InMemory/SQLite để xác nhận ràng buộc đặc thù PostgreSQL; bước triển khai model cần test tích hợp trên PostgreSQL thật.

## 4 nhóm ưu tiên

Workbook có 21 use case gồm cả 4 use case lịch sử nội bộ:

| Nhóm | Use case | Các bảng dự kiến |
| --- | --- | --- |
| Tài khoản và người dùng | UC-TK-01…06 | `roles`, `user_profiles`, `accounts`, `account_history`, `password_reset_requests` |
| Xác thực | UC-XT-01…03 | `auth_sessions`, `auth_login_throttles`, `auth_history` |
| Phòng ban | UC-PB-01…06 | `departments`, `department_history` |
| Khu vực | UC-KV-01…06 | `areas`, `area_history` |

### Tài khoản và xác thực

Migration seed 3 role cố định: ID 1 `QTTB_MANAGER` (Trưởng phòng QTTB), ID 2 `QTTB_STAFF` (Nhân viên phòng QTTB), ID 3 `DEPARTMENT_STAFF` (Nhân viên phòng ban). Code khai báo trong `Core/Accounts/RoleCodes.cs`. Không seed account hoặc password; bootstrap Trưởng phòng đầu tiên cần use case riêng.

Một account có một role, một profile và một phòng ban trực thuộc bắt buộc. Email phải duy nhất sau chuẩn hóa, kể cả tài khoản khóa. Password khởi tạo bằng email đã chuẩn hóa theo tài liệu hiện tại và chỉ lưu hash. Đặt lại trực tiếp bởi Trưởng phòng và quên mật khẩu bằng token một lần là hai luồng riêng. Việc đổi/đặt lại mật khẩu và khóa account phải thu hồi phiên.

Mô hình hiện tại dùng session token được băm trong DB. Mỗi account chỉ có tối đa một phiên hiệu lực; đăng nhập mới thu hồi phiên cũ. Handler xác thực session kiểm tra session còn hạn/còn hoạt động và `accounts.is_lock` trong mỗi request cần bảo vệ; Nhóm 2 bổ sung đăng nhập, đăng xuất, throttle và đóng phiên hết hạn. Base chưa mặc định thay cơ chế này bằng JWT/refresh token hoặc ASP.NET Identity.

5 lần đăng nhập sai liên tiếp trong 10 phút dẫn tới tạm chặn 15 phút; trạng thái này độc lập với `is_lock`. Cần xử lý đồng thời khi tăng counter, đăng nhập mới, thu hồi session và hoàn tất reset token. Không được tiết lộ email có tồn tại qua lỗi đăng nhập.

`SESSION_ENDED` chỉ được ghi một lần đầu tiên cho một session. Password/hash/token không được xuất qua DTO hoặc snapshot audit. Bootstrap Trưởng phòng đầu tiên cần quy trình riêng trước khi mở chức năng tạo account.

### Phòng ban

Phòng ban là đơn vị tổ chức. Tên duy nhất toàn hệ thống sau chuẩn hóa khoảng trắng và hoa/thường, kể cả phòng ban khóa. Khóa phòng ban ngăn gán tài khoản/nghiệp vụ mới nhưng giữ account, thiết bị và phiếu cũ; không tự khóa nhân viên. Cả ba role được đọc danh mục cơ bản; thông tin nhân sự và audit cần quyền riêng.

Nhóm 3 triển khai tại UseCases/Departments và DepartmentsController; repository lọc/sắp xếp/phân trang/ProjectTo tại DB. DepartmentTransaction lấy cùng advisory lock với AccountTransaction, kiểm tra lại phiên và quyền trong giao dịch ghi. Tạo/sửa/khóa/mở khóa và audit thành công được commit nguyên tử; lỗi/từ chối lưu riêng sau rollback. Không có chức năng xóa phòng ban; request DELETE chỉ được ghi DELETE_DENIED và trả 403. Lịch sử chỉ đọc bởi Trưởng phòng. Số liên kết thiết bị/phiếu hiện chưa có dữ liệu để thống kê, API cảnh báo trả null thay vì 0. Tài liệu input/output testcase được lưu riêng bên ngoài project.

### Khu vực

Khu vực là cây vật lý nhiều cấp với `parent_id` tùy chọn; nhãn Tòa/Tầng/Phòng không ép cứng số cấp. Phòng ban và khu vực độc lập. Mã node không đổi khi chuyển nhánh; tên duy nhất trong cùng cha sau chuẩn hóa, bao gồm các node gốc.

Chuyển cha phải ngăn tự trỏ/chu trình và bảo đảm các tổ tiên đích còn hoạt động. Trạng thái hiệu lực bằng trạng thái node kết hợp toàn bộ tổ tiên. Khóa một node không ghi đè trạng thái lưu của con. Di chuyển nhánh giữ định danh/liên kết và ghi một sự kiện cho node được di chuyển, với snapshot đường dẫn cũ/mới; không viết lại lịch sử cũ.

### Audit

Giữ 4 bảng riêng: `account_history`, `auth_history`, `department_history`, `area_history`. Dữ liệu thay đổi và audit thành công cần cùng transaction. Lỗi/từ chối có luồng lưu riêng sau rollback; không ghi SUCCESS giả cho thao tác đọc hoặc thao tác không thay đổi gì.

Các bảng lịch sử bất biến đối với chức năng quản lý thường. Snapshot phải chọn lọc trường, không serialize toàn bộ entity. Chuyển phòng ban của account chỉ ghi trong account history; chuyển nhánh khu vực không sinh sự kiện giả cho từng hậu duệ.

## Schema và migration đã hoàn thiện

1. `accounts.normalized_email` là generated stored column `lower(btrim(email))`, có unique index không lọc trạng thái khóa. Lookup sử dụng khóa chuẩn hóa. `TextNormalization` trong Core và DbContext chuẩn hóa giá trị khi ghi qua EF; cột generated bảo vệ cả ghi SQL trực tiếp.
2. `departments.normalized_name` và `areas.normalized_name` được PostgreSQL sinh bằng NFC, gộp khoảng trắng, trim và lower. Tên phòng ban unique toàn hệ thống. Khu vực dùng hai partial unique index: tên node gốc khi cha NULL và cặp parent/name khi cha khác NULL. Các khóa chuẩn hóa là shadow property của EF, không thêm vào DTO nghiệp vụ.
3. `auth_history.session_id` có partial unique index khi `action_code='SESSION_ENDED'`; các unique correlation index giữ theo DBML.
4. Session unique theo `account_id WHERE is_active`; reset request unique theo `account_id WHERE NOT is_closed`. Không dùng `now()` trong index. Use case sau này phải đóng session/reset cũ kể cả đã hết hạn, rồi cấp mới trong transaction phù hợp; phải xử lý cạnh tranh và kiểm tra expiry khi sử dụng.
5. Khu vực có check chống tự trỏ, FK chống cha không tồn tại, trigger chống chu trình và advisory lock transaction trước mọi ghi vào bảng `areas`. Trigger VOLATILE kiểm tra snapshot mới sau khi chờ lock. Transaction ghi cây dùng READ COMMITTED (mặc định); isolation level khác bị từ chối để tránh snapshot cũ. SQL trigger nằm trong migration, cần giữ khi cập nhật schema; `EnsureCreated` không tạo những trigger này.
6. Có check tên/mã/email/hash không rỗng theo mapping, email throttle ở dạng chuẩn hóa và failed_count không âm. Các kiểu PostgreSQL và native enum giữ theo DBML. Kiểm tra dữ liệu đầy đủ ở API/use case, quyền và điều kiện trạng thái vẫn triển khai sau.
7. Foreign key dùng Restrict. Các use case sau này không xóa cứng account/phòng ban/khu vực. Quyền DB và chính sách giữ audit sẽ cấu hình khi triển khai; migration không tự tạo account quản trị.

Migration có Down để xóa trigger/function, bảng và enum; rollback/reapply được kiểm tra trên DB test dùng một lần. Mỗi thay đổi schema tiếp theo tạo migration mới; không sửa migration đã áp dụng vào môi trường dùng chung.

## Thứ tự triển khai đề xuất

1. Schema, migration đầu và seed 3 role đã hoàn thành. Tiếp theo xây quy trình bootstrap Trưởng phòng đầu tiên.
2. Đã có băm mật khẩu, xác thực phiên DB, phân quyền tài khoản, đăng nhập/cấp phiên, đăng xuất, chặn sai 5 lần trong 10 phút trong 15 phút và đóng phiên hết hạn thuộc Nhóm 2.
3. Tài khoản/người dùng và phòng ban đã có API, service, DTO, audit và kiểm thử PostgreSQL. Tiếp tục triển khai cây khu vực; tích hợp thống kê và kiểm tra gán mới cho thiết bị/phiếu khi các module này có dữ liệu.
4. Kiểm thử API và ràng buộc trên PostgreSQL, cập nhật Swagger và cấu hình SMTP cho quên mật khẩu; xem tài liệu module tài khoản.

Các nghiệp vụ thiết bị, mượn trả, cấp phát, điều chuyển, sửa chữa và thanh lý được bổ sung sau khi có yêu cầu đã chỉnh sửa; tài liệu cũ chỉ cho thấy cần giữ khả năng mở rộng liên kết thiết bị/phòng ban/khu vực và lịch sử.

## Tài liệu kỹ thuật

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Npgsql EF Core 10 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Swagger/OpenAPI trong ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/tutorials/web-api-help-pages-using-swagger?view=aspnetcore-10.0)
