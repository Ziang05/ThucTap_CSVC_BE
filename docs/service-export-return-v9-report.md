# Báo cáo triển khai BT/SC – Xuất – Nhập lại v9

Ngày: 04/11/2026. Repository: `/home/bao-bui/Projects/thuctap/MainProject/ThucTap_CSVC_BE`.

## 1. Kết quả và giới hạn cần đọc trước

Đã thay workflow cũ bằng chuỗi chứng từ độc lập: tạo nguồn → duyệt toàn nguồn và giữ thiết bị → tạo PX riêng → duyệt PX → kết quả xuất từng item → phiếu nhập lại từng phần. Kết quả chuyên môn của nguồn được xử lý riêng với việc giao/nhận vật lý. Hold nguồn chỉ xóa khi item cuối và không có guard; endpoint đối soát xử lý hold giữ lại sau nhập về.

**Chưa bảo đảm loại trừ nghiệp vụ hai chiều trên toàn hệ thống.** BT/SC/PX từ chối thiết bị đã có operation hold, nhưng code cũ của Điều chuyển/Thu hồi, Mượn, Thiết bị và Tiếp nhận chưa đọc service hold/guard mới. Các module đó có thể ghi sau khi BT/SC đã giữ thiết bị. Đây là giới hạn do phạm vi sửa đã được người dùng quy định; không có thay đổi âm thầm ở bảng/repository operation hold hay module độc lập. Test characterization xác nhận lỗ hổng này, không được hiểu rằng test pass chứng minh toàn hệ thống đã an toàn.

Chưa áp migration vào database ứng dụng; chưa reset/reseed database ứng dụng; không commit/push. Dữ liệu người dùng đang sửa trước đợt này phải được bảo toàn khi đồng bộ.

## 2. Khảo sát và quyết định triển khai

Nền kiến trúc gồm Core → UseCases → Infrastructure/API, ASP.NET Core/EF Core 10 và PostgreSQL. Sử dụng service/repository/controller và transaction sẵn có, không thay kiến trúc. Đã dùng Graphify và Serena trong khảo sát, kiểm tra caller/reference rồi đọc source thực tế. Các tài liệu, entity và migration được đối chiếu với DBML v9 và bốn tab đặc tả.

| Nội dung | Trước đợt v9 | Sau đợt v9 |
|---|---|---|
| Tạo nguồn | Bắt buộc phân External vào PX lồng trong request | Chỉ source/items/history; External có thể chưa có PX |
| Duyệt nguồn | Tạo operation hold, khởi động PX con | Tạo service hold đúng item; không tạo/duyệt PX |
| Kết quả nguồn | Completed/Failed; export Failed tự NotExecuted | Người dùng chọn Completed/Failed/NotExecuted có mô tả/lý do |
| Kết quả PX | Một kết quả ở header | Từng item Success/Failed, header Completed sau tất cả item |
| Sửa/hủy PX | Thiếu luồng độc lập | UC-PX-09/10 với quyền, version, guard diff, hash/audit |
| Nhập lại | Không có chứng từ riêng | Header/items/history riêng, nhận từng phần, không duyệt |
| Giải phóng hold | Kết thúc nguồn xóa operation hold ngay | Chỉ xóa service hold khi item cuối và không có guard; có đối soát |

Yêu cầu trực tiếp và v9 được ưu tiên khi tài liệu có đoạn cũ: tab liên kết còn nhắc reservations v8; DBML có ghi chú giới hạn Export của đợt sửa cũ. Đợt hiện tại được yêu cầu triển khai Export và Return đầy đủ trong phạm vi riêng. Không suy diễn tạo nhanh Thu hồi thành backend tự tạo/link chứng từ.

## 3. Phạm vi file

Các đường dẫn trong mục này là tương đối từ repository nêu ở đầu báo cáo, để session sau tra cứu.

### Core và API

| File | Thay đổi |
|---|---|
| `src/CSVC.Core/ServiceOrders/ServiceOrder.cs` | Metadata PX mới, kết quả từng item, navigation nhập lại và báo cáo tùy chọn |
| `src/CSVC.Core/ServiceOrders/ServiceOrderHistory.cs` | Export history có item/target/hash |
| `src/CSVC.Core/ServiceOrders/ServiceDeviceCustody.cs` | Service hold, export guard, return header/item/history |
| `src/CSVC.API/Controllers/ServiceOrdersController.cs` | Endpoint đối soát hold |
| `src/CSVC.API/Controllers/ExportSlipsController.cs` | Tạo/duyệt/sửa/hủy PX, kết quả từng item, item đủ điều kiện nhập lại |
| `src/CSVC.API/Controllers/ExportReturnsController.cs` | Tạo/danh sách/chi tiết/lịch sử return |

### UseCases

| File | Thay đổi |
|---|---|
| `src/CSVC.UseCases/ServiceOrders/ServiceOrderService.cs` | Workflow nguồn, quyền hiện tại, hold, kết quả chuyên môn, đối soát |
| `src/CSVC.UseCases/ServiceOrders/ServiceOrderService.Exports.cs` | Workflow PX, hash, diff, audit, kết quả và guard |
| `src/CSVC.UseCases/ServiceOrders/ServiceOrderService.Returns.cs` | Return theo PX, nhận từng phần, thời gian/người nhận độc lập |
| `src/CSVC.UseCases/ServiceOrders/IServiceOrderService.cs` | Hợp đồng endpoint mới |
| `src/CSVC.UseCases/ServiceOrders/DTOs/ServiceOrderRequests.cs` | Validation/hợp đồng request v9 |
| `src/CSVC.UseCases/ServiceOrders/DTOs/ServiceOrderResponses.cs` | Kết quả item và chứng từ return, metadata nhận về |
| `src/CSVC.UseCases/Abstractions/Persistence/IServiceOrderRepository.cs` | Truy vấn source/return/history và batch tra cứu nguồn |

### Persistence và kiểm thử

| File | Thay đổi |
|---|---|
| `src/CSVC.Infrastructure/Persistence/Repositories/ServiceOrderRepository.cs` | Query mới; batch nạp source khi liệt kê, tránh truy vấn riêng cho từng dòng; MAX sequence ở DB |
| `src/CSVC.Infrastructure/Persistence/Configurations/ServiceOrderConfigurations.cs` | Check/index/FK v9; giữ repair/report linkage |
| `src/CSVC.Infrastructure/Persistence/Configurations/ServiceDeviceCustodyConfigurations.cs` | Mapping năm entity mới, composite FK |
| `src/CSVC.Infrastructure/Persistence/Migrations/20261104025216_ServiceExportReturnV9.cs` | Migration schema và gọi SQL bảo toàn dữ liệu |
| `src/CSVC.Infrastructure/Persistence/Migrations/20261104025216_ServiceExportReturnV9.Designer.cs` | Model tại migration |
| `src/CSVC.Infrastructure/Persistence/Migrations/CsvcDbContextModelSnapshot.cs` | Model mới, tổng 52 entity |
| `src/CSVC.Infrastructure/Persistence/Migrations/ServiceExportReturnIntegritySql.cs` | Preflight, deferred consistency, immutability, down hỗ trợ |
| `tests/CSVC.API.Tests/ServiceOrderApiTests.cs` | Thay các ca workflow cũ bằng kiểm thử v9 |
| `tests/CSVC.API.Tests/AccountApiTestHost.cs` | Bơm lỗi lưu export/return history để thử rollback |
| `tests/CSVC.API.Tests/DeviceQuantityApiTests.cs` | Chuẩn bị Settings QTTB cho ca gọi source mới; giữ nguyên kiểm tra Quantity=0 bị từ chối |
| `tests/CSVC.API.Tests/DedicatedRepositoryTests.cs` | Kiểm tra 52 entity và năm bảng mới |

Tài liệu: `docs/service-orders-and-exports-api.md` được cập nhật hợp đồng v9; `docs/service-orders-validation.md` được đánh dấu lịch sử trước v9; báo cáo hiện tại là tài liệu bàn giao.

Không sửa production service/repository/configuration của Transfer/Recovery, Loan, Device, Receipt hoặc DamageReport; không sửa `InventoryTransaction` dùng chung, `DeviceOperationHoldRepository`, bảng `device_operation_holds`, hay migration cũ của Nhóm 14. DbContext tự nhận configurations qua assembly; không cần thêm DbSet hay thay DI.

## 4. Invariants đã thực thi

| Chủ đề | Thực thi |
|---|---|
| Một source giữ thiết bị | PK service hold theo device; UNIQUE item; composite FK chứng minh cùng source/item/device; toàn bộ approve trong một transaction |
| Xung đột nguồn/PX | Kiểm tra operation hold, service hold, guard; PX chỉ chấp nhận hold đúng chủ, không bỏ qua hold bằng một cờ client |
| Giữ thiết bị đang ở ngoài | Success → AwaitingReturn; source Completed không giải phóng guard/hold còn cần |
| Kết quả chuyên môn | Kết quả cuối cần recorder/time/description; NotExecuted có lý do; PX Failed không tự sửa source |
| Kết thúc nguồn/PX | Deferred constraints kiểm tra trạng thái với toàn bộ item; không tạo trạng thái Completed khi còn item chưa có kết quả |
| Sửa PX | PendingApproval + source InProgress; nguồn bất biến; diff nonempty; quyền creator/manager; version; guard đúng chủ; EDIT trước/sau lưu ID dòng |
| Hủy PX | PendingApproval; lý do/người/thời gian; giữ header/items/history; chỉ xóa guard của PX đó |
| Nhập lại đúng nguồn | Composite FK cả header/source và export item/source; UNIQUE export item trên toàn bộ return |
| Điều kiện nhận về | Chỉ Success chưa nhập lại và guard AwaitingReturn; Quantity/condition/source không tự đổi |
| Hai mốc thời gian | createdAt server, receivedAt user UTC không tương lai; cho ghi nhận muộn; creator/receiver khác nhau được |
| Lịch sử và bất biến | Return/header/items và history cấm UPDATE/DELETE; kết quả cuối bất biến; owner hold/guard bất biến; source config khóa sau duyệt |
| Release | DELETE hold chỉ item cuối + không có guard + HOLD_RELEASED audit; return không đụng service hold; reconcile xử lý ở BT/SC |

Transaction dùng account/session advisory lock rồi inventory advisory lock của nền hiện có; các device row được khóa theo UUID ổn định. Do khóa inventory dùng chung, mutation hiện được tuần tự hóa toàn cục; cải thiện throughput cần một công việc kiến trúc riêng. Retry source dựa trên create hash/metadata kết quả; PX và return có canonical hash bền. EDIT không đổi nội dung và reconcile không có gì để xóa vẫn có chứng cứ NoChange để retry ổn định.

## 5. Điểm tích hợp ngoài phạm vi — chưa sửa

Các vị trí dưới đây phải được cập nhật trong một thay đổi được cho phép riêng. Tên method là mốc bền vững hơn số dòng.

| File/method hiện tại | Vấn đề | Đề xuất riêng |
|---|---|---|
| `src/CSVC.UseCases/DeviceTransfers/DeviceTransferService.cs` — `ValidateTransferRequestAsync`, `ReserveAsync`, `HandoverCompleteAsync` | Kiểm tra/giữ operation hold, không đọc service hold hoặc export guard; nhánh Recovery dùng cùng service | Sau khóa device, kiểm tra cả hold/guard cho từng thiết bị và subtree; không cho ngoại lệ chỉ vì đích QTTB. Ngoại lệ cùng nguồn phải có đặc tả ownership/authorization riêng |
| `src/CSVC.UseCases/DeviceLoans/DeviceLoanService.cs` — `ValidateLoanRequestAsync`, quyết định duyệt, giao/nhận | Có thể giữ operation hold sau source đã giữ service hold | Dùng cùng policy khả dụng sau khóa thiết bị; từ chối service hold/guard ở create/edit/approve/handover |
| `src/CSVC.UseCases/Devices/DeviceService.cs` — cập nhật cấu hình, cây cha/con, lock | Hiện kiểm tra operation hold/pending workflow; không hiểu source Completed vẫn giữ hold vì đang ở ngoài | Chặn sửa thiết bị/cây theo hai hold và guard; bao gồm toàn subtree và cha mới |
| `src/CSVC.UseCases/DeviceReceipts/DeviceReceiptService.cs` — validate existing device, duyệt/hoàn tất | Chưa hiểu service hold/guard; không được dùng Receipt thay Return | Revalidate ownership/availability sau khóa; giữ độc lập chứng từ nhập lại thuộc Export |
| `src/CSVC.Infrastructure/Persistence/Repositories/DeviceOperationHoldRepository.cs` — `ExistsForDeviceAsync`, `TryAcquireAsync` và `src/CSVC.Infrastructure/Persistence/Repositories/InventoryRepositories.cs` — projection availability/`HasPendingWorkflowAsync` | Repository giữ và màn hình khả dụng chỉ thấy dữ liệu cũ; có thể hiển thị thiết bị đang service là Available | Thêm policy availability/hold thống nhất (không đổi nghĩa mù quáng ExistsForDevice), cập nhật projection; lock-check-acquire trong cùng transaction ở mọi caller |

Các role/session/settings writers dùng khóa account chung; BT/SC sử dụng lại để tránh kiểm quyền đồng thời thay đổi. Một kiểm tra SELECT đơn lẻ hai bảng không đủ nếu không cùng khóa với writer.

**Test characterization:** sau approve source, cố ý gọi đường ghi operation hold hiện hữu cho cùng device. Việc ghi vẫn thành công vì module cũ chưa được sửa; sau đó create PX bị BT/SC/PX từ chối. Test này làm rõ chiều còn thiếu để session sau không hiểu sai.

## 6. Migration và vận hành

Migration phụ thuộc chuỗi hiện có, gồm `DamageReportsGroup14`. Up từ chối nếu còn source/PX legacy hoặc operation hold kind ServiceOrder. Không backfill, không đổi nghĩa dữ liệu cũ, không tự reset. Down từ chối khi còn chứng từ v9; khi dữ liệu rỗng, gỡ trigger v9 rồi phục hồi membership cũ. Các repair subtype trigger và report-device linkage giữ nguyên.

Đã thực hiện thử migration trên PostgreSQL riêng, mỗi test tạo database có tên ngẫu nhiên rồi hủy database đó. Database ứng dụng không được dùng cho thử nghiệm. EF migration nhận diện mất dữ liệu là dự kiến vì bỏ header result cũ; preflight bảo đảm việc bỏ cột không âm thầm mất chứng từ cũ.

Trước khi áp cho môi trường đích: xác minh connection string và xác nhận đây là test có thể bỏ, xử lý dữ liệu cũ bằng quy trình reset/reseed được cho phép, cấu hình phòng QTTB, rồi chạy migration bằng deployment command hiện có. Không dùng database production để bỏ qua preflight. API không tự migrate khi khởi động thường; `--migrate` là thao tác triển khai riêng.

Tích hợp frontend phải đổi hợp đồng: bỏ exportSlips trong create source, tạo PX bằng endpoint riêng, ghi kết quả bằng items, tạo return riêng với receivedAt/receiver, gọi reconcile cho nguồn cuối còn hold sau return. Không gửi source ID mới trong EDIT; dùng itemNo đúng nguồn. Xem [API v9](service-orders-and-exports-api.md) để lấy body mẫu và toàn bộ route.

## 7. Kiểm chứng

Kết quả cuối được ghi ở mục bên dưới sau khi chạy build/test và cập nhật Graphify. Các ca bao gồm concurrency approve nguồn/PX/return, edit/cancel với stale version, retry lịch sử/no-change, kết quả mixed, return từng phần/trùng/khác nguồn/tương lai, phân biệt creator/receiver/time, rollback khi audit lỗi, hold release trước/sau return, report link đúng thiết bị, migration down/up/preflight và regression các module còn lại.

### Kết quả cuối tại repository đích

| Hạng mục | Kết quả |
|---|---|
| `dotnet build CSVC.sln -m:1 --disable-build-servers -p:NuGetAudit=false -v minimal` | Thành công, 0 warning, 0 error; SDK 10.0.114 |
| Toàn bộ `CSVC.API.Tests` | **235 passed, 0 failed, 0 skipped** trên PostgreSQL 18.6 riêng |
| Riêng `ServiceOrderApiTests` v9 | **33 passed**, bao gồm ca characterization giới hạn cross-module |
| Các test còn lại | 202 passed, gồm regression Nhóm 14 và các module hiện có |
| `git diff --check` | Qua |
| Đồng bộ source | 27 file, có kiểm tra SHA-256 trước/sau ghi; không có file baseline ngoài phạm vi bị thay đổi |
| Graphify | `graphify update .` thành công: 4.797 nodes, 15.922 edges, 208 communities |
| Database ứng dụng | Không áp migration, không reset/reseed |

Lệnh test được chạy với `CSVC_POSTGRES_TEST_CONNECTION` trỏ cụm thử nghiệm riêng tại localhost:55484; mỗi case tạo và xóa database ngẫu nhiên của nó. TRX: `tests/CSVC.API.Tests/TestResults/v9-final.trx` (artifact cục bộ, không cần commit). Build dùng package đã cache; `NuGetAudit=false` chỉ để tránh truy vấn mạng của audit trong môi trường hạn chế, không phải kết quả quét lỗ hổng thư viện.

Graphify update chỉ tái trích xuất code bằng AST; không gọi LLM bên ngoài và không làm mới ngữ nghĩa tài liệu. Không chạy gợi ý label/semantic extraction yêu cầu gửi nội dung ra ngoài. Snapshot/Designer migration và code hiện tại đã qua kiểm tra pending model changes trong tests.

Các file cũ được sao lưu trước đồng bộ dưới `/tmp/csvc-v9-target-backup/`; manifest hash ở `/tmp/v9-synced.json`. Không tạo commit hoặc push. Các thay đổi Nhóm 14 đã có trước đợt v9 được giữ nguyên.

## 8. Công việc kế tiếp được tách riêng

Phần source v9 trong phạm vi BT/SC–Xuất–Nhập lại đã được triển khai và kiểm chứng. Trước khi xem cơ chế giữ thiết bị là bảo vệ xuyên module, cần một thay đổi được cho phép riêng ở các điểm mục 5: khóa–kiểm tra cả hai hold/guard–ghi cùng transaction. Sau đó kiểm thử cả hai thứ tự cạnh tranh BT/SC trước/sau Transfer/Loan/Device và rà soát ngoại lệ Recovery cùng nguồn. Không tự coi đích QTTB là bằng chứng được phép vượt hold.

Để chạy thử với dữ liệu thật của môi trường test đích, người vận hành phải xác minh đúng môi trường, chuẩn bị dữ liệu legacy theo preflight và cấu hình phòng QTTB trước khi áp migration. Đợt này không thực thi bước đó lên database ứng dụng.

