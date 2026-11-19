# Nhập về QTTB rồi cấp phát riêng

Task 3 tách đơn vị đề xuất khỏi đơn vị thực tế đang giữ thiết bị. Quy tắc này áp dụng cho phiếu nhập mới; hồ sơ, chứng từ và lịch sử cũ được bảo toàn theo phiên bản luồng.

## Phiếu mới

- Payload tiếp tục dùng `requestingDepartmentCode` để chọn **đơn vị đề xuất**. Không nhận phòng tiếp nhận hoặc phiên bản luồng từ client.
- Backend chọn phòng tiếp nhận từ Settings, lưu `ReceivingDepartmentId` và `workflowVersion=2`. Phải cấu hình QTTB đang hoạt động; nhân viên/quản lý thao tác ghi trên phiếu mới phải thuộc đúng phòng đó.
- Duyệt chưa làm tăng số lượng hoặc chuyển trách nhiệm cho đơn vị đề xuất. Hoàn tất nhập toàn phiếu mới đổi số lượng 0 → 1, gán phòng đang giữ là QTTB và `useState=InStorage`.
- Có thể chọn hồ sơ đã khai báo với số lượng 0 ở phòng khác. Khi thực nhận mới chuyển hồ sơ về QTTB; giữ mã/mã vạch/metadata, bỏ vị trí và người chịu trách nhiệm cũ nếu đổi phòng, gán người tiếp nhận làm người phụ trách giữ. Hồ sơ 0 không phải bằng chứng đã nhận/cấp phát vật lý.
- Thiết bị khai báo ngay trên phiếu cũng được tạo tại QTTB khi hoàn tất. Đơn vị đề xuất chưa được quyền quản lý/xem chi tiết thiết bị đã nhập về QTTB chỉ vì đã đề xuất mua.
- Tạo phiếu cấp phát riêng bằng luồng Allocation hiện có. Bên nhận đồng ý chưa đổi phòng; xác nhận thực nhận mới cập nhật phòng/người chịu trách nhiệm, ghi `useState=InUse` và lịch sử bàn giao. Số lượng vẫn là 1.

## QTTB tự sử dụng và phân loại thiết bị

Device list/detail/barcode thêm `useState`; list có filter `useState=Unspecified|InStorage|InUse` chạy trước count/phân trang. Đây là phân loại sử dụng, độc lập với condition, status và availability.

| Giá trị | Ý nghĩa |
| --- | --- |
| Unspecified | Chưa ghi nhận phân loại; giữ cho dữ liệu cũ và hồ sơ chưa tiếp nhận, không tự suy diễn từ phòng ban. |
| InStorage | Đã tiếp nhận và QTTB đang giữ chờ cấp phát/đưa vào dùng; thu hồi về QTTB cũng ghi giá trị này. |
| InUse | Đã xác nhận nhận cấp phát/điều chuyển hoặc đã được đưa vào sử dụng trong chính phòng QTTB. |

`POST /api/v1/devices/{deviceCode}/put-into-use` ghi nhận QTTB tự sử dụng, nhận:

```json
{ "responsibleAccountCode": "TK_STAFF", "areaCode": null, "notes": null }
```

Chỉ QTTB staff đúng phòng được xác nhận. Thiết bị phải có số lượng 1, Active, Good, InStorage, thuộc QTTB và không có operation/service hold. Người chịu trách nhiệm phải đang hoạt động, thuộc QTTB; vị trí nếu chọn phải hoạt động cùng các tổ tiên. Toàn bộ kiểm tra và thay đổi nằm trong inventory transaction/khóa thiết bị.

Thành công trả 204, đổi sang InUse và ghi `DEVICE_PUT_INTO_USE` cùng người/vị trí/ghi chú; không đổi phòng, không tạo phiếu điều chuyển nguồn/đích trùng nhau. Gọi lại sau thành công trả conflict, không thêm lịch sử giả. Lỗi audit rollback cả trạng thái và người/vị trí. Đây là sự kiện riêng sau nhập, không tự đưa vào dùng trong lần nhập kho.

Frontend có nút **Dùng tại QTTB** ở chi tiết thiết bị đủ điều kiện, danh sách chọn người chịu trách nhiệm/vị trí, cùng nhãn và lọc mục đích sử dụng. Form nhập có **Đơn vị đề xuất** và **Phòng tiếp nhận thực tế** chỉ đọc. Danh sách thiết bị chưa tiếp nhận vẫn là dropdown chọn trực tiếp, không yêu cầu nhập mã.

## Phiếu cũ và migration

Migration `ReceiptEquipmentCustody` thêm các trường và đánh dấu mọi phiếu đã có là `workflowVersion=1`, `receivingDepartmentCode/Name=null`. Không đổi đơn vị, mã, số lượng, dòng phiếu, giữ chỗ, liên kết cũ hoặc lịch sử thiết bị.

Phiếu phiên bản 1, kể cả đang chờ/đã duyệt, tiếp tục tiếp nhận vào phòng đã ghi ở `requestingDepartmentCode` theo quy tắc cũ. Không diễn giải lại thành nhập về QTTB; UI sửa phiếu cũ vẫn chọn hồ sơ cùng phòng trên phiếu. Thiết bị cũ giữ Unspecified cho đến khi có nghiệp vụ mới xác nhận một thay đổi sử dụng. Không đoán thiết bị QTTB đang dùng hay giữ từ tên/phòng hiện tại.

DTO phiếu trả `workflowVersion`, `receivingDepartmentCode`, `receivingDepartmentName` bên cạnh trường yêu cầu cũ. Không cần client gửi lại các trường mới khi sửa. Đổi phòng QTTB trong Settings bị chặn khi còn phiếu nhập phiên bản 2 chờ/đã duyệt hoặc thiết bị InStorage tại phòng hiện tại, ngoài ràng buộc phiếu điều chuyển mở đã có. Lỗi mới: `EQUIPMENT_DEPARTMENT_HAS_RECEIVING_WORK`.

Build image và chạy migration theo [hướng dẫn release](device-workflows-release.md), triển khai frontend cùng phiên bản. Kiểm thử dùng database tạm; chưa áp migration hoặc thay image API đang chạy. Khi rollback, cần phục hồi đúng phiên bản schema/code và dữ liệu từ backup nếu đã có phiếu luồng mới, vì schema cũ không lưu riêng phòng tiếp nhận và phân loại sử dụng.

Task này chưa thêm module yêu cầu/task hoặc giả lập PMMS. Quy tắc kết thúc toàn bộ yêu cầu được triển khai khi có module yêu cầu, không suy ra từ việc hoàn tất một phiếu nhập.

## Kiểm chứng

Build backend và frontend đạt. Toàn solution backend đã chạy qua 277 kiểm thử; sau khi hoàn thiện audit và kiểm tra cấu hình, nhóm nhập kho/migration liên quan chạy lại đạt 14/14. Các ca mới xác nhận phòng đề xuất chưa nhận quyền quản lý khi chỉ nhập/duyệt, cấp phát cần xác nhận thực nhận, QTTB tự dùng có sự kiện riêng, giữ nghĩa phiếu cũ, chặn payload giả mạo và rollback khi audit thất bại. Frontend đạt 56/56 kiểm thử hợp đồng, có kiểm tra phòng nhận chỉ đọc, chọn hồ sơ chưa tiếp nhận và payload đưa vào sử dụng.


## Trạng thái Docker local

Migration và image API local đã được cập nhật; xem [biên bản đồng bộ và kiểm tra](local-runtime-sync.md). Ghi chú chưa triển khai ở phần trước mô tả thời điểm hoàn thành mã nguồn; trạng thái môi trường hiện tại theo mục này.

## Nghiệm thu lại ở CV-05

Đã chạy lại kiểm thử tiếp nhận/cấp phát sau CV-02, gồm giữ nghĩa phiếu phiên bản 1, chọn hồ sơ chưa tiếp nhận, giữ mã/mã vạch/metadata, quyền/cấu hình QTTB, duyệt chưa đổi trách nhiệm, thực nhận mới đổi phòng, dùng tồn, QTTB tự dùng và rollback. Các phiếu nhiều dòng vẫn xử lý đủ; chọn thiết bị không tự mở rộng cây tham chiếu cũ.

Kịch bản trình duyệt dùng API/PostgreSQL thật kiểm tra nhập mới về QTTB/InStorage, chặn đóng sớm và hủy sau nhập, cấp phát riêng, thực nhận/InUse rồi mới đóng yêu cầu; đồng thời kiểm tra QTTB tự dùng. Chỉ dùng PMMS giả lập trong database kiểm thử riêng sau khi thông báo. Kết quả tại [biên bản CV-02–CV-05](cv02-05-validation.md).
