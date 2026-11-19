# Nhóm 19 — Kết quả kiểm chứng ngày 19/11/2026

## Môi trường

.NET SDK 10.0.115, net10.0, EF Core 10.0.13/Npgsql 10.0.3, PostgreSQL 18.6 chạy riêng trên cổng 55419. Các test tạo database ngẫu nhiên rồi tự xóa; không đọc hoặc áp dụng migration vào database ứng dụng đang sử dụng. Không commit hoặc push.

## Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Build toàn solution | Thành công, 0 lỗi biên dịch; giữ cảnh báo NU1903 của AutoMapper bằng tham số `WarningsNotAsErrors=NU1903`. |
| Test Nhóm 19 | 11/11 integration test qua, dùng PostgreSQL thật; nhiều assertions theo các UC và các nhánh lỗi. |
| Hồi quy cuối tại repo đích | 299/300 qua, không skip, 2 phút 25 giây; lỗi duy nhất đã tái hiện độc lập trên source gốc chưa tích hợp Nhóm 19. |
| Migration Up | Thành công từ chuỗi migration hiện có; chỉ thêm 10 bảng kiểm kê và các đối tượng module. |
| Migration Down → Up | Thành công trên database `nhom19_migration` tạm. |
| Model/snapshot | `has-pending-model-changes`: không có thay đổi; test cũng kiểm không pending migration. |
| Script SQL idempotent | Áp dụng thành công lần đầu và lần chạy lại trên database nhom19_script, ON_ERROR_STOP bật. |
| Graphify | Đồng bộ thành công mã nguồn: 5.527 nodes, 18.741 edges, 241 communities; graph.json, graph.html dạng gộp nhóm và GRAPH_REPORT.md đã cập nhật. Không gọi LLM bên ngoài, chi phí token LLM của bước update = 0. |
| Nguồn Drive | V7 + bốn tài liệu V3 đã đọc; bốn file trong ZIP khớp nội dung bản rời. |

`InventoryApiTests` kiểm tra lifecycle, sửa nháp/owner, revision/approval/rejection, PB scope, lead time/policy, roster gốc và lịch sử sửa, hoàn thành bất biến, xuất báo cáo snapshot, idempotency, ghi đồng thời, lỗi audit/notification rollback, guards SQL, reminders/read inbox. Hai ca bổ sung kiểm đúng ngưỡng 72 giờ và việc thiết bị live đổi PB/tình trạng/quantity không thay đổi kết quả/snapshot hoặc bị module kiểm kê ghi đè.

## Vấn đề đã có trước, không thuộc Nhóm 19

1. `ServiceOrderApiTests.Export_requires_existing_active_supplier_and_stores_its_id_and_server_snapshots`, dòng 163: test kỳ vọng PostgreSQL SQLSTATE `23503` khi xóa nhà cung cấp có tham chiếu; PostgreSQL 18.6 trả `23001`. Đã chạy đúng test trên repo gốc trước khi đưa bất kỳ file Nhóm 19 nào vào và nhận cùng lỗi, cùng dòng. Giữ nguyên test và nghiệp vụ xuất phiếu/nhà cung cấp theo phạm vi yêu cầu.
2. AutoMapper 16.2.0 đang có trong repo bị NuGet audit báo NU1903 mức high, advisories `GHSA-6vhm-j9h7-f9gh`, `GHSA-gpwp-5797-mjff`. Build mặc định bị TreatWarningsAsErrors chặn. Chỉ dùng tham số lệnh để giữ cảnh báo và kiểm chứng code; không đổi dependency, không đưa cấu hình bỏ audit vào source.
3. Máy có giới hạn 128 inotify instances. Lượt chạy hồi quy đầu gặp giới hạn khi tạo nhiều test host. Lượt kiểm chứng dùng `DOTNET_PROCESSOR_COUNT=2`, `DOTNET_USE_POLLING_FILE_WATCHER=1`; đây là cấu hình lệnh test, không phải thay đổi API.

## Quyết định giữ theo đặc tả

Không thay assignee sau START; chưa có ngoại lệ completion cho NOT_CHECKED; PB chưa được xem biên bản sau completion. Dự án chưa có notification push/email chung: đã triển khai in-app/reminder, chưa thêm kênh ngoài ứng dụng. Các chi tiết API và điều chỉnh schema nằm trong `inventory-api.md`, `inventory-schema.dbml`.

Graphify update là code-only: không tuyên bố đã tái phân tích ngữ nghĩa các tài liệu Markdown/DBML mới. Các file định dạng hạ tầng không được parser hỗ trợ đã được CLI báo bỏ qua.
