# Thiết bị độc lập và tham chiếu cũ

Mỗi thiết bị/bộ có một hồ sơ, mã và mã vạch riêng, số lượng 0 hoặc 1. Thay thế, nâng cấp, bổ sung hoặc tháo bộ phận được ghi bằng can thiệp trước–sau và chứng từ vật tư. Không cần khai báo bộ phận thành thiết bị con để thực hiện nghiệp vụ.

- `ParentDeviceId` và `ParentDeviceCode` đã lưu được giữ để tra cứu lịch sử. Không gộp, xóa, đổi định danh hoặc chuyển hồ sơ cũ thành vật tư.
- API gắn/đổi cha trả `409 DEVICE_COMPONENT_RELATIONSHIPS_RETIRED`. UI chỉ hiển thị tham chiếu cũ, không có form sửa liên kết.
- Phiếu nhập mới không nhận liên kết cha–con. Khi sửa phiếu cũ, backend giữ tham chiếu đã lưu; khi hoàn tất không gắn lại quan hệ vào thiết bị.
- Chọn, khóa, mượn, điều chuyển và thu hồi xử lý đúng các thiết bị được chọn. Thiết bị có tham chiếu cũ vẫn được chọn độc lập; tình trạng/giữ chỗ của một thiết bị không tự chặn thiết bị khác.
- Phiếu đã lưu nhiều dòng vẫn phải xử lý đủ các dòng đó, kể cả phiếu trước đây đã được mở rộng từ cây. Không tự thu gọn nội dung phiếu hoặc giải phóng giữ chỗ đang có.
- Tham chiếu tới thiết bị thuộc phòng khác được giữ trong DB nhưng thông tin hiển thị vẫn theo quyền đọc hiện có.

Migration `DevicePackagesReadOnlyReferences` bổ sung guard chặn ghi liên kết mới hoặc sửa liên kết cũ tại DB, không cập nhật/xóa dữ liệu hiện có. Guard này là chính sách mới sau CV-01; không tái dùng hai trigger stash cũ đã được gỡ trong CV-01. `Down` chỉ gỡ guard mới.

Kiểm chứng bằng các test migration, chọn độc lập, khóa, giữ chỗ, phiếu nhiều thiết bị và rollback. Xem [biên bản CV-02–CV-05](cv02-05-validation.md).
