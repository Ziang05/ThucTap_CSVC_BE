# Bảo trì, sửa chữa và phiếu xuất

Backend triển khai thiết kế `service_orders` / `service_order_items` chung cho bảo trì và sửa chữa. Phiếu xuất thuộc đúng một phiếu nguồn; mỗi item External nằm trong đúng một phiếu xuất. Không có API sửa phiếu, tạo phiếu xuất độc lập hoặc hủy phiếu xuất độc lập.

## Endpoint và quyền

Dùng session Bearer, JSON camelCase và enum PascalCase như các module hiện tại. Mã phiếu BT/SC/PX do server sinh, dài 32 ký tự. Route dùng mã phiếu; kết quả item dùng `itemNo` trong chi tiết phiếu. Không trả UUID/FK của đối tượng nghiệp vụ.

| Method | Route dưới `/api/v1` | Quyền |
|---|---|---|
| GET | `/service-orders`, `/service-orders/{code}` | QTTB Manager/Staff |
| POST | `/service-orders` | QTTB Staff |
| POST | `/service-orders/{code}/approve` | QTTB Manager |
| POST | `/service-orders/{code}/cancel` | QTTB Staff đã tạo phiếu |
| POST | `/service-orders/{code}/results` | QTTB Staff |
| GET | `/service-orders/{code}/history` | QTTB Manager/Staff |
| GET | `/export-slips`, `/export-slips/{code}` | QTTB Manager/Staff |
| POST | `/export-slips/{code}/result` | QTTB Staff |
| GET | `/export-slips/{code}/history` | QTTB Manager/Staff |

Chính sách v1 áp dụng theo các vai trò QTTB hiện có; Department Staff không truy cập nhóm API này. Mỗi mutation đọc lại account, session và role trong inventory transaction, dùng cùng khóa với thay đổi quyền và nghiệp vụ nhập/mượn/điều chuyển.

Danh sách phiếu nguồn lọc bằng `search` (mã phiếu), `orderType`, `status`; danh sách xuất lọc bằng `serviceOrderCode`, `status`. Cả hai hỗ trợ `page` (mặc định 1) và `pageSize` (mặc định 20, tối đa 100), sắp xếp mới nhất rồi mã phiếu. History dùng cùng tham số phân trang. Response phân trang có `items`, `totalCount`, `page`, `pageSize`.

## Tạo phiếu

```json
{
  "requestId": "57135411-d8c3-479d-a15f-cd047d52e777",
  "orderType": "Repair",
  "notes": "Sửa thiết bị đợt 1",
  "items": [
    { "deviceCode": "TB001", "executionMethod": "OnSite", "workDescription": "Kiểm tra nguồn" },
    { "deviceCode": "TB002", "executionMethod": "External", "workDescription": "Thay bo mạch" }
  ],
  "exportSlips": [
    {
      "facilityName": "Trung tâm dịch vụ A",
      "facilityAddress": "Địa chỉ cơ sở",
      "facilityContact": "Liên hệ cơ sở",
      "deviceCodes": ["TB002"],
      "plannedExportAt": "2026-11-05T08:00:00+07:00"
    }
  ]
}
```

`orderType`: `Maintenance` hoặc `Repair`. `executionMethod`: `OnSite` hoặc `External`. Tối đa 100 item và 100 phiếu xuất trong một phiếu nguồn. Mã thiết bị trim/uppercase; không cho trùng. Mọi item External phải xuất hiện đúng một lần trong `exportSlips`; item OnSite không được xuất hiện. Phiếu chỉ gồm OnSite gửi `exportSlips: []`.

Thiết bị phải Active, không Missing và chưa bị operation khác giữ. Maintenance yêu cầu Good; Repair chấp nhận Good/Damaged. Điều kiện được kiểm tra lại khi duyệt. Pending chưa giữ thiết bị; giữa các yêu cầu đang chờ, phiếu được duyệt thành công trước sẽ giữ thiết bị. Sửa quan hệ cha/con thiết bị tính cả phiếu dịch vụ đang chờ thông qua `HasPendingWorkflowAsync`.

Tạo phiếu nguồn, item, repair detail, phiếu xuất, line xuất và history trong một transaction. Tên/mã/serial thiết bị và thông tin cơ sở được lưu snapshot. Các thay đổi master sau đó không sửa chứng từ cũ.

## Duyệt, hủy và kết quả

Duyệt dùng `{ "requestId": "<UUID>", "version": 0 }`. Chuyển cả phiếu nguồn, mọi item và mọi phiếu xuất sang `InProgress`, đồng thời giữ thiết bị qua `DeviceOperationHold` loại `ServiceOrder`. Nếu bất kỳ thiết bị nào không còn hợp lệ hoặc đã bị giữ, toàn bộ thao tác rollback.

Hủy dùng `{ "requestId": "<UUID>", "reason": "Không còn nhu cầu", "version": 0 }`. Chỉ creator được hủy khi PendingApproval; tất cả phiếu xuất con chuyển `Canceled`. Item chưa có execution status vẫn null. Không hủy sau duyệt.

Kết quả xuất:

```json
{
  "requestId": "9fc3e176-cc9f-4331-99ef-88dbf156ae31",
  "result": "Success",
  "recipientName": "Người tiếp nhận",
  "recipientContact": "Số liên hệ",
  "evidenceRef": "handover/document-001",
  "version": 1
}
```

`Success` yêu cầu `recipientName`, không nhận `failureReason`; chỉ kết thúc phiếu xuất. Item nguồn vẫn `InProgress` và thiết bị vẫn bị giữ để chờ kết quả chuyên môn. `Failed` yêu cầu `failureReason`, không nhận người nhận; các item thuộc đúng phiếu xuất đó chuyển `NotExecuted` và được giải phóng hold. `evidenceRef` là tham chiếu chứng từ dạng text, không nhận absolute URL và không upload file.

Ghi kết quả chuyên môn theo một hoặc nhiều item:

```json
{
  "requestId": "09ed2b51-d519-4eba-90dd-74a23e4cc583",
  "items": [
    { "itemNo": 1, "result": "Completed", "description": "Đã xử lý nguồn" },
    { "itemNo": 2, "result": "Failed", "description": "Không thể sửa bo mạch" }
  ]
}
```

Chỉ cho `Completed` hoặc `Failed`, luôn cần mô tả. Không cho người dùng tự chọn `NotExecuted`. Item External chỉ được ghi kết quả sau khi phiếu xuất tương ứng Success. Một item terminal không được ghi lại bằng yêu cầu mới. Ghi nhiều item là atomic; thiết bị được giải phóng hold khi item terminal. Khi tất cả item terminal, phiếu nguồn tự `Completed`, kể cả khi có item Failed/NotExecuted. `Completed` của phiếu nguồn nghĩa là đã kết thúc xử lý, không đồng nghĩa mọi thiết bị thành công.

Module không tự thay đổi tình trạng vật lý thiết bị, phòng ban hoặc người chịu trách nhiệm khi ghi kết quả; DBML chưa quy định chính sách cho các thay đổi này.

## Idempotency, version và audit

Mọi mutation bắt buộc có `requestId` UUID khác rỗng trong body. Đây là khóa idempotency nghiệp vụ, độc lập với header `X-Request-Id` do middleware sinh cho HTTP. Client giữ nguyên body và khóa khi retry cùng thao tác; dùng khóa mới cho thao tác mới. Create lưu thêm `create_request_hash` để phân biệt payload retry đã chuẩn hóa. Retry thành công không tạo phiếu con, sự kiện hay cập nhật version lần hai. Retry create trả lại chứng từ hiện tại; retry mutation trả 204.

Các khóa được phân phạm vi theo thao tác và đối tượng, với unique index cho create/approve/cancel và export result. Kết quả bulk dùng chung request ID cho nhiều item. Dùng lại khóa với payload hoặc actor khác trả 409 `IDEMPOTENCY_CONFLICT` khi phát hiện retry trên cùng đối tượng; unique index chặn xung đột khóa trên các đối tượng khác. Không dùng khóa này như bằng chứng phân quyền: quyền luôn kiểm tra lại trước khi trả kết quả retry.

`version` tùy chọn cho transition/result; nếu có thì phải khớp version hiện tại của phiếu nguồn hoặc phiếu xuất đang thao tác. Mỗi item cũng có concurrency version. Lỗi version trả 409 `VERSION_CONFLICT`; retry cùng khóa thành công được nhận diện trước kiểm tra version.

Snapshot hiện tại vẫn lưu người tạo/duyệt/ghi kết quả và thời gian. History lưu từng sự kiện thành công trong cùng transaction với nghiệp vụ, gồm before/after allowlist. Sự kiện `ITEM_NOT_EXECUTED` liên kết phiếu xuất gây ra thay đổi. Unique history có cả target để một request ghi nhiều item/phiếu xuất không tự xung đột. Phiên bản này chưa bổ sung audit Denied/Failed cho các thao tác bị từ chối/rollback.

Lỗi chính: 400 `VALIDATION_FAILED`; 401 `AUTHENTICATION_REQUIRED`; 403 `WORKFLOW_ACTION_FORBIDDEN`; 404 `SERVICE_ORDER_NOT_FOUND`/`EXPORT_SLIP_NOT_FOUND`/`DEVICE_NOT_FOUND`; 409 `INVALID_WORKFLOW_TRANSITION`, `DEVICE_BUSY`, `DEVICE_LOCKED`, `DEVICE_NOT_AVAILABLE`, `EXPORT_NOT_SUCCESSFUL`, `VERSION_CONFLICT`, `IDEMPOTENCY_CONFLICT`; lỗi database/ghi audit trong inventory transaction trả 503 `DEVICE_WORKFLOW_UNAVAILABLE` và rollback.

## Schema và triển khai

Migration `ServiceOrdersAndExports` thêm 7 bảng, index và FK thiết bị/tài khoản. Enum mới lưu string PascalCase với CHECK constraints, dùng chung native `audit_outcome` hiện có. DBML dùng enum uppercase tương đương về nghĩa. Giữ hai FK đơn trên line xuất; PostgreSQL deferred constraint triggers kiểm tra item External, cùng phiếu nguồn và repair detail chỉ thuộc phiếu Repair, kể cả khi header/quan hệ bị cập nhật trực tiếp.

`facility_id` và `damage_report_id` là dependency ngoài phạm vi chưa có master tương ứng. API không nhận hai ID này; facility lấy snapshot, repair detail để `damage_report_id = null`. Chưa có frontend cho module này.

Migration chỉ được kiểm tra trên database tạm; không tự áp lên database ứng dụng. Triển khai bằng quy trình migration hiện có sau khi cấu hình đúng database và sao lưu theo quy trình của dự án:

```bash
dotnet run --project src/CSVC.API -- --migrate
```

Kiểm thử:

```bash
# CSVC_POSTGRES_TEST_CONNECTION trỏ tới server test cho phép tạo database dùng một lần.
dotnet test CSVC.sln --filter FullyQualifiedName~ServiceOrderApiTests
```

Test tự tạo/xóa database riêng, không dùng database ứng dụng. Có kiểm tra create/retry, role/version, bulk audit, xuất thành công/thất bại, nhiều nhóm xuất, cạnh tranh hold, rollback audit, ràng buộc SQL và migration down/up.
