# Backend BT/SC – Xuất – Nhập lại, v9

Cập nhật ngày 04/11/2026. Tài liệu này thay thế hợp đồng API cũ tạo PX cùng nguồn và ghi kết quả xuất ở header.

## 1. Nguồn và phạm vi

- Thiết kế dữ liệu: `CSVC_v9.dbml` được người dùng cung cấp.
- Use case: [Nhóm 13 – Bảo trì](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=2026103013), [Nhóm 15 – Sửa chữa](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=2026103015), [Nhóm 18 – Xuất/Nhập lại](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=2026103018), [Liên kết](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=2026110419).
- Ưu tiên yêu cầu triển khai trực tiếp và v9: một số đoạn ở bảng liên kết vẫn nhắc `service_order_device_reservations` của v8; ghi chú v9 có đoạn giới hạn sửa Export của một đợt cũ. Đợt này người dùng yêu cầu sửa cả BT/SC, Export và Return, dùng `service_order_device_holds`.
- Không sửa nghiệp vụ Thu hồi/Điều chuyển, Mượn, Tiếp nhận độc lập, Thiết bị, Báo cáo sự cố; không sửa bảng/repository `device_operation_holds`. Đọc báo cáo để gắn vào item Repair chỉ là phụ thuộc đọc của BT/SC.
- Framework thực tế: .NET/ASP.NET Core/EF Core 10; PostgreSQL. Giao dịch dùng cơ chế khóa hiện có của repository.

## 2. Quyền và nguyên tắc API

Mọi endpoint cần Bearer session đang hoạt động; tài khoản không khóa; vai trò QTTB Manager hoặc Staff; phòng ban hiện tại trùng phòng QTTB được chọn trong Settings. Không có cấu hình phòng QTTB thì trả conflict yêu cầu cấu hình trước. Nhân viên phòng ban không được truy cập module này.

Manager duyệt toàn nguồn và toàn PX. Cả Manager/Staff được tạo, ghi kết quả, nhập lại và đối soát. Hủy nguồn chỉ người tạo. Sửa/hủy PX chỉ người tạo hoặc Manager, khi PX PendingApproval và nguồn InProgress.

API sử dụng mã công khai và `itemNo` của item nguồn, không yêu cầu người dùng nhập UUID nội bộ. Trong một PX, itemNo vẫn là số của item nguồn; trong một phiếu nhập lại, itemNo tham chiếu item của PX được chọn. Snapshot EDIT trong lịch sử có ID nội bộ của dòng cũ/mới để phục dựng việc thêm/bỏ dòng; ID này không dùng làm tham số chọn thiết bị. JSON lịch sử không lưu contact, token hay mật khẩu.

Mutation cần `requestId` UUID khác rỗng. Retry dùng lại đúng key/payload/tài khoản; đổi nội dung trả 409 `IDEMPOTENCY_CONFLICT`. Tạo nguồn/PX/return dùng hash canonical; thao tác PX dùng hash lưu bền trong lịch sử, kể cả EDIT không làm thay đổi dữ liệu. Version cũ trả 409 `VERSION_CONFLICT`; EDIT bắt buộc version. Các transition khác nhận version tùy chọn và vẫn được tuần tự hóa bằng transaction/khóa.

Base URL bên dưới: `/api/v1`. Danh sách/lịch sử dùng `page` từ 1 và `pageSize` 1–100. Mutation thành công trả 204, create trả 201; reconcile trả 200 `{ "released": n }`.

## 3. BT/SC

| Method | Đường dẫn | Hành vi |
|---|---|---|
| GET | `/service-orders`, `/service-orders/{code}` | Danh sách/chi tiết; lọc search, orderType, status |
| POST | `/service-orders` | Tạo header + items + history, PendingApproval |
| POST | `/service-orders/{code}/approve` | Duyệt cả nguồn; tạo service hold từng thiết bị |
| POST | `/service-orders/{code}/cancel` | Hủy nguồn PendingApproval, bắt buộc reason |
| POST | `/service-orders/{code}/results` | Ghi kết quả chuyên môn từng item |
| POST | `/service-orders/{code}/reconcile-holds` | Đối soát hold của đúng một nguồn |
| GET | `/service-orders/{code}/history` | Lịch sử nguồn, gồm HOLD_ACQUIRED/HOLD_RELEASED |

Tạo nguồn:

```json
{
  "requestId": "10000000-0000-0000-0000-000000000001",
  "orderType": "Repair",
  "items": [
    { "deviceCode": "TB_A", "executionMethod": "OnSite", "workDescription": "Kiểm tra", "damageReportCode": "BC_A" },
    { "deviceCode": "TB_B", "executionMethod": "External", "workDescription": "Sửa ngoài" }
  ],
  "notes": "Ghi chú"
}
```

`damageReportCode` tùy chọn, chỉ dành cho Repair và báo cáo phải đúng thiết bị; không sửa/đóng báo cáo. Source create không nhận `exportSlips`; client gửi hợp đồng cũ bị từ chối. Thiết bị nguồn có thể còn ở phòng khác, nhưng phải Quantity=1, Active, điều kiện phù hợp: Maintenance chỉ Good; Repair Good/Damaged. Missing không hợp lệ.

Duyệt:

```json
{ "requestId": "10000000-0000-0000-0000-000000000002", "version": 0 }
```

Toàn bộ item chuyển InProgress, mỗi item có `service_order_device_holds` lưu thiết bị, nguồn, item, người duyệt, thời gian. Không tạo/duyệt PX hay Thu hồi. Kiểm tra cả operation hold, service hold, export guard dưới cùng transaction và khóa thiết bị theo thứ tự ổn định.

Ghi kết quả:

```json
{
  "requestId": "10000000-0000-0000-0000-000000000003",
  "version": 1,
  "items": [ { "itemNo": 1, "result": "NotExecuted", "description": "Không đủ điều kiện thực hiện" } ]
}
```

Kết quả: Completed/Failed/NotExecuted; description bắt buộc, đặc biệt lý do NotExecuted. External Completed/Failed cần một lần xuất Success. Khi có guard HeldForExport, phải xử lý kết quả PX trước khi kết thúc item chuyên môn. Success còn chờ về vẫn được ghi kết quả chuyên môn, nhưng giữ service hold. Source Completed chỉ khi mọi item có kết quả cuối.

Service hold được xóa ngay khi item cuối và không có export guard; việc xóa và HOLD_RELEASED cùng transaction. Nếu còn guard thì giữ hold. Sau nhập lại, gọi reconcile bằng body transition ở trên: chỉ xóa hold của item cuối và không còn guard; không đổi kết quả hay trạng thái nguồn. Retry trả lại số đã giải phóng của lần gốc. Không có worker tự động: frontend/vận hành cần chủ động gọi endpoint này cho nguồn đã hoàn thành còn hold.

Tạo nhanh Thu hồi chỉ là frontend mở biểu mẫu với dữ liệu điền sẵn. Backend BT/SC không gọi, sinh hoặc liên kết chứng từ Thu hồi.

## 4. Phiếu xuất

| Method | Đường dẫn | Hành vi |
|---|---|---|
| GET | `/export-slips`, `/export-slips/{code}` | Danh sách/chi tiết; lọc serviceOrderCode, status |
| POST | `/export-slips` | Tạo PX riêng cho một nguồn InProgress |
| POST | `/export-slips/{code}/approve` | Manager duyệt toàn PX |
| PUT | `/export-slips/{code}` | UC-PX-09: sửa PX PendingApproval |
| POST | `/export-slips/{code}/cancel` | UC-PX-10: hủy PX PendingApproval |
| POST | `/export-slips/{code}/result` | Ghi Success/Failed theo từng item |
| GET | `/export-slips/{code}/history` | CREATE/EDIT/CANCEL/APPROVE/ITEM_RESULT_RECORDED/EXPORT_COMPLETED |
| GET | `/export-slips/{code}/return-eligible-items` | Các item Success chưa nhập lại |

Tạo PX:

```json
{
  "requestId": "20000000-0000-0000-0000-000000000001",
  "serviceOrderCode": "SC_SOURCE",
  "facilityName": "Cơ sở sửa chữa A",
  "facilityAddress": "Địa chỉ",
  "facilityContact": "Liên hệ",
  "plannedExportAt": "2026-11-05T08:00:00+07:00",
  "itemNos": [2],
  "notes": "Giao đợt 1"
}
```

Item phải External/InProgress, đúng nguồn, có service hold đúng chủ; thiết bị hiện ở phòng QTTB, Quantity=1, Active, không Missing, không operation hold/guard khác. Tạo PX đặt HeldForExport. Duyệt kiểm tra lại; chuyển toàn PX InProgress, không ghi kết quả hộ item.

Sửa PX dùng cùng bộ trường cơ sở/lịch/ghi chú/itemNos và thêm requestId/version, **không có serviceOrderCode**. Nguồn bất biến. Diff trong cùng transaction: giữ guard/dòng còn lại, xóa guard/dòng bỏ, thêm guard/dòng mới; tập item không rỗng. EDIT có snapshot trước/sau và hash, giữ được retry key cũ sau các lần sửa tiếp theo. EDIT không đổi nội dung chỉ ghi NoChange, không tăng version.

Hủy PX:

```json
{ "requestId": "20000000-0000-0000-0000-000000000002", "version": 0, "reason": "Cơ sở ngừng nhận" }
```

Lưu Canceled/người/thời gian/lý do, giữ header/items/history, chỉ giải phóng guard đúng dòng của PX đó; không xóa service hold. Không được hủy PX đã duyệt bằng UC này.

Kết quả xuất:

```json
{
  "requestId": "20000000-0000-0000-0000-000000000003",
  "version": 1,
  "items": [
    { "itemNo": 2, "result": "Success", "recipientName": "Người nhận", "recipientContact": "Liên hệ", "evidenceRef": "DOC_ABC" },
    { "itemNo": 3, "result": "Failed", "failureReason": "Chưa nhận thiết bị" }
  ]
}
```

Success cần người nhận, không failureReason; guard chuyển AwaitingReturn. Failed cần lý do, không người nhận/contact; xóa guard đúng dòng. Evidence là mã tham chiếu tài liệu, không URL tuyệt đối. Metadata/kết quả nằm ở item; kết quả cuối bất biến. Header Completed khi tất cả item có kết quả, chấp nhận hỗn hợp. Failed không đổi kết quả nguồn hoặc xóa service hold; có thể tạo PX khác nếu nguồn/item vẫn InProgress. Sau Success đã nhập lại, cũng có thể xuất lại nếu nguồn/item vẫn InProgress.

## 5. Phiếu nhập lại

| Method | Đường dẫn | Hành vi |
|---|---|---|
| GET | `/export-return-slips` | Danh sách; lọc exportSlipCode |
| GET | `/export-return-slips/{code}` | Chi tiết chứng từ |
| POST | `/export-return-slips` | Ghi nhận Completed ngay, không duyệt |
| GET | `/export-return-slips/{code}/history` | RETURN_SLIP_RECORDED/RETURN_ITEM_RECEIVED |

```json
{
  "requestId": "30000000-0000-0000-0000-000000000001",
  "exportSlipCode": "PX_SOURCE",
  "handoverPersonName": "Người giao về",
  "handoverPersonContact": "Liên hệ",
  "receivedByAccountCode": "TK_QTTB",
  "receivedAt": "2026-11-06T09:00:00+07:00",
  "notes": "Nhập đợt 1",
  "items": [ { "itemNo": 2, "conditionAtReturn": "Damaged", "returnNote": "Còn lỗi" } ]
}
```

Chỉ một PX mỗi chứng từ, không cần chờ PX có kết quả tất cả item nếu các item chọn đã Success. Chọn Success chưa nhập lại, guard AwaitingReturn đúng dòng. Một PX có nhiều phiếu nhập lại từng phần; một dòng xuất chỉ được nhập lại đúng một lần trên toàn hệ thống.

`createdAt` là thời gian server ghi chứng từ; `receivedAt` do người dùng xác nhận thời gian thực nhận, chuyển UTC, không được ở tương lai so với server. Hai mốc có thể khác nhau. `createdByAccountCode` lấy từ session; `receivedByAccountCode` có thể khác, nhưng phải là tài khoản QTTB hoạt động trong phòng hiện tại.

Header/items/history và xóa đúng guard trong một transaction. Return không cập nhật thiết bị (condition/department/quantity), kết quả nguồn hay service hold. ConditionAtReturn chỉ là dữ liệu của chứng từ. Return và lịch sử đã ghi là bất biến, không có API sửa/xóa.

## 6. Database, concurrency và migration

Migration: `20261104025216_ServiceExportReturnV9`.

- Thêm `service_order_device_holds`, `export_device_guards`, `export_return_slips`, `export_return_slip_items`, `export_return_history`.
- Chuyển kết quả/metadata xuất từ header xuống item; thêm metadata create/approve/edit/cancel và hash/target của export history.
- Bỏ UNIQUE đơn `export_slip_items.service_order_item_id`, giữ UNIQUE(PX, item nguồn). PK guard theo device bảo đảm một lượt xuất đang giữ/chưa về. UNIQUE dòng nhập lại theo export item bảo đảm nhận về một lần.
- Composite FK hold(item, source, device) → source item; return item(header, export) → return header; return item(export item, export) → export item. Tất cả Restrict.
- Giữ các CHECK cũ về subtype, trạng thái, số thứ tự, cơ sở, cancel; giữ trigger repair subtype và Group14 report-device linkage. Thay membership cũ bằng deferred consistency cho trạng thái nguồn/PX, hold/guard, Success/return và audit nhập lại. Trigger cấm sửa/xóa return/history, cấm đổi chủ guard/hold, khóa cấu hình nguồn đã duyệt và kết quả cuối.

Các mutation dùng `IInventoryTransaction` hiện có: khóa advisory account/session rồi inventory, khóa các hàng device theo UUID đã sắp xếp, kiểm tra lại, save, commit. Không sửa wrapper dùng chung hoặc các module khác. Khóa inventory hiện tại tuần tự hóa mutation toàn hệ thống; đây là giới hạn throughput sẵn có của codebase. Unique/version và trigger là lớp bảo vệ database bổ sung.

**Migration không backfill và không tự xóa dữ liệu.** Prepare từ chối nếu còn bất kỳ nguồn/PX cũ hoặc operation hold ServiceOrder. Downgrade cũng từ chối khi còn chứng từ v9. Đã kiểm tra down/up trên database tạm. Chỉ reset/reseed sau khi người vận hành xác minh môi trường đó là test có thể bỏ; không thực thi reset lên database ứng dụng trong đợt này. Không chạy `--migrate` lên database có dữ liệu cũ mà chưa thực hiện quy trình này.

## 7. Giới hạn tích hợp cần xử lý riêng

Đọc [báo cáo triển khai v9](service-export-return-v9-report.md). Các module cũ chưa tự có kiểm tra service hold/guard mới; một module cũ có thể tạo operation hold sau khi BT/SC đã giữ thiết bị. BT/SC/PX có kiểm tra operation hold chiều ngược lại, nhưng không thể bảo đảm loại trừ hai chiều toàn hệ thống nếu không sửa các điểm ghi ngoài phạm vi. Không triển khai đồng thời các luồng xung đột như thể cơ chế hold toàn hệ thống đã hoàn chỉnh.
