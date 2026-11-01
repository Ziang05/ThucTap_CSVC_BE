# Kiểm tra backend bảo trì, sửa chữa và xuất

Ngày: 02/11/2026 (Asia/Ho_Chi_Minh). Branch: `baobui_br`.

## Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Build và chạy test module mới trực tiếp tại repo | 21/21 đạt, không skip |
| Toàn bộ suite sau thay đổi, trên bản mã có cùng nội dung | 164 test: 160 đạt, 4 thất bại, không skip |
| Đối chiếu mã gốc, chỉ sửa test helper để biên dịch được | 143 test: 138 đạt, 5 thất bại, không skip |
| Migration lên, xuống rồi lên lại trên database tạm | Đạt; giữ nguyên thiết bị có sẵn |
| Snapshot EF khớp model runtime | Đạt |
| Kiểm tra khoảng trắng diff | Đạt |

Môi trường: .NET SDK 10.0.114; PostgreSQL 18 tạm, locale `en_US.UTF-8`. Các test tự tạo/xóa database riêng. Không chạy migration hoặc thay dữ liệu trên database ứng dụng.

## Bốn lỗi đã có trên mã gốc

- `AreaApiTests.Own_and_effective_locks_remain_distinct_and_do_not_rewrite_child_states`: kỳ vọng số thiết bị null, thực tế 0.
- `ManufacturerApiTests.List_filters_sorts_pages_and_lock_impact_reports_unknown_device_counts`: kỳ vọng null, API trả số.
- `CategoryApiTests.List_filters_sorts_pages_and_lock_impact_reports_unknown_device_counts`: kỳ vọng null, API trả số.
- `SupplierApiTests.List_filters_sorts_pages_and_lock_impact_reports_unknown_device_counts`: kỳ vọng null, API trả số.

Lỗi thứ năm của mã gốc là test schema còn kỳ vọng 18 entity dù branch đã có 31. Test đã được cập nhật cho 38 entity (thêm 7 bảng mới). Không thay hành vi đếm của các module danh mục trong đợt triển khai này.

Trước khi chạy được baseline, cần cập nhật `RepositoryTests.CreateUnitOfWork`: helper cũ chưa truyền các inventory repository mà constructor hiện tại yêu cầu. Thay đổi này được giữ trong repo; mã production của UnitOfWork không thay đổi.

## Phạm vi kiểm thử mới

21 trường hợp bao gồm: create/retry cùng payload và chống dùng lại khóa với dữ liệu khác; snapshot bất biến; lọc danh sách; quyền và version; duyệt toàn phiếu; hủy cascade; hold độc quyền khi duyệt đồng thời; xuất Success vẫn giữ item InProgress; xuất Failed chỉ đổi đúng nhóm; bulk result và history không xung đột; tự hoàn thành nguồn; rollback create/approve/export khi ghi audit lỗi; ràng buộc PostgreSQL cho cùng nguồn/External/Repair; migration down/up; trạng thái vật lý/khóa thiết bị; từ chối URL chứng từ tuyệt đối và NotExecuted nhập tay.

## Giới hạn còn lại

- Chưa có frontend cho nhóm chức năng mới.
- Master cơ sở dịch vụ và báo hỏng chưa có trong repo: dùng facility snapshot, chưa nhận facilityId/damageReportId từ API.
- History hiện ghi các sự kiện thành công; chưa bổ sung audit Denied/Failed cho yêu cầu bị từ chối hoặc rollback.
- Chưa áp migration lên database ứng dụng. Xem [tài liệu API](service-orders-and-exports-api.md) trước khi triển khai.
- Graphify CLI update chỉ cập nhật cấu trúc mã nguồn/tài liệu; không khẳng định đã làm mới phân tích ngữ nghĩa tài liệu bằng LLM.
