# Đồng bộ API và database Docker local

## Trạng thái hiện tại sau CV-01

API local chạy tại `http://localhost:8080`, live/ready đạt; database không còn migration nguồn chờ áp dụng. Đã áp `MaterialInventory`, `EquipmentInventoryGroup19`, `ReadableBusinessCodes`, `RemoveStaffAssignments`, `AlignProcurementCatalogReferences` và `ReconcileLegacyComponentGuards` sau khi backup, diễn tập trên bản sao và được chủ project xác nhận.

Dữ liệu thiết bị/chứng từ giữ nguyên; phân công cũ được lưu ở `retired_assignment_context`. Hai guard của stash cũ đã được gỡ để schema khớp hành vi của code đã pull, vẫn giữ tham chiếu/lịch sử migration. Không chạy PMMS giả lập trên database chính. Kết quả test, bản sao và giới hạn phục hồi tại [báo cáo CV-01](cv01-validation.md).

## Ghi nhận trước CV-01 — chỉ tham khảo lịch sử

Ở thời điểm ghi nhận cũ dưới đây, module phân công đã gỡ khỏi source nhưng `RemoveStaffAssignments` chưa được áp vào local. Trạng thái này đã được thay thế bằng kết quả CV-01 bên trên.

Lỗi 500 khi đọc phiếu nhập/yêu cầu PMMS xuất phát từ API mới chạy trên schema cũ: thiếu `device_receipts.receiving_department_id` và các bảng yêu cầu/công việc. Không phải lỗi dữ liệu nhập trên giao diện.

Đã sao lưu PostgreSQL bằng `pg_dump -Fc`, kiểm tra danh mục backup bằng `pg_restore -l` và lưu bản sao ngoài repository trong `%LOCALAPPDATA%/CSVC/backups`.

Đã build lại image `csvc-api:local`, dừng API, chạy migration một lần rồi khởi động lại API. Bốn migration đã áp dụng trên môi trường local:

- `RetireDeviceComponentRelationships`
- `ReceiptEquipmentCustody`
- `SimulatedPmmsRequests`
- `QttbWorkTasks`

Kiểm tra sau cập nhật bằng đăng nhập hiện có: health live/ready, danh sách phiếu nhập, yêu cầu PMMS, việc của nhân viên, danh sách và báo cáo công việc của quản lý đều trả HTTP 200. Phiếu nhập cũ vẫn đọc được. Không chạy seed hoặc gửi sự kiện PMMS giả lập; các phiên đăng nhập kiểm tra đã được đăng xuất.



Khi cập nhật những lần sau, triển khai schema và image API cùng phiên bản theo [hướng dẫn triển khai](device-workflows-release.md). API không tự áp dụng migration khi khởi động bình thường. Trạng thái trong tài liệu này áp dụng cho Docker local.
