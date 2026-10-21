# Quy ước phát triển cho team

## Code đặt ở đâu?

| Tầng | Trách nhiệm |
| --- | --- |
| `Core` | Entity, enum, quy tắc của đối tượng nghiệp vụ và `DomainException`. |
| `UseCases` | Service xử lý nghiệp vụ, DTO, `UseCaseException`; interface repository/dịch vụ ngoài đặt trong `Abstractions`. |
| `Infrastructure` | Triển khai các interface; DbContext, mapping, repository, Unit of Work và migration nằm trong `Persistence`. |
| `API` | Controller nhận request, gọi use case và trả response; middleware, Swagger và cấu hình HTTP. |

Hướng tham chiếu: `UseCases → Core`, `Infrastructure → UseCases + Core`, `API → UseCases + Infrastructure`.

- Core độc lập với các tầng còn lại. UseCases không tham chiếu Infrastructure, EF Core hoặc HttpContext.
- Controller gọi use case; nghiệp vụ đặt trong UseCases/Core, truy vấn EF đặt trong repository.
- Tổ chức code theo nhóm chức năng: `Accounts`, `Authentication`, `Departments`, `Areas`.

## Khi thêm chức năng

1. Thêm/sửa entity trong `Core/<Nhóm>` và mapping trong `Infrastructure/Persistence/Configurations` nếu cần.
2. Thêm hàm truy vấn vào interface repository trong `UseCases/Abstractions/Persistence`; triển khai tại `Infrastructure/Persistence/Repositories`. Repository riêng kế thừa `GenericRepository<T>`.
3. Nếu thêm repository mới, đăng ký trong `Infrastructure/DependencyInjection.cs`, bổ sung property vào `IUnitOfWork` và inject vào `UnitOfWork`.
4. Viết service và DTO trong `UseCases/<Nhóm>`, đăng ký service trong `UseCases/DependencyInjection.cs`, rồi thêm endpoint trong `API/Controllers`.
5. Nếu thay đổi schema, tạo migration mới, kiểm tra SQL và áp dụng. Giữ nguyên migration đã áp dụng vào môi trường dùng chung.

## Khi xử lý dữ liệu

- Dùng `unitOfWork.Accounts`, `unitOfWork.Departments`…; use case gọi `CommitAsync()` sau khi hoàn tất thao tác. Repository không tự lưu.
- Dữ liệu thay đổi và audit thành công phải lưu cùng transaction. Nhiều lần commit hoặc thao tác đồng thời cần thiết kế transaction/locking phù hợp.
- Trả DTO riêng, không trả entity trực tiếp hoặc đưa password/hash/token vào response hay audit. Giữ quy định khóa thay cho xóa cứng account, phòng ban, khu vực.
- Truyền `CancellationToken` xuống repository; dùng `TimeProvider` cho thời gian và `IRequestContext` cho mã truy vết.

Trước khi gửi thay đổi, chạy `dotnet build CSVC.sln` và `dotnet test CSVC.sln`; thay đổi ràng buộc DB cần kiểm tra trên PostgreSQL thật. Không commit `.env`, credentials, `bin/`, `obj/`.

Cách chạy: [README](README.md). Quy tắc schema và nghiệp vụ: [kiến trúc](docs/architecture.md).
