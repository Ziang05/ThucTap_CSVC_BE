# Devices API

API quản lý hồ sơ tài sản thiết bị, được implement theo contract tại [device-workflows-contract.md](device-workflows-contract.md). Tất cả route yêu cầu session bearer hiện có. Actor, role và phòng ban của Department Staff được xác định từ session/database, không nhận từ request.

Base path: `/api/v1/devices`

## Số lượng theo từng thiết bị

- Mỗi mã là một thiết bị riêng, `quantity` chỉ nhận `0` hoặc `1` và do backend quản lý.
- Tạo hồ sơ qua `POST /devices` luôn có `quantity=0`. List, detail và barcode lookup đều trả trường này; không nhận sửa số lượng trong request tạo/sửa.
- Hoàn tất phiếu tiếp nhận mới đổi `0 → 1`. Thiết bị đã tiếp nhận không được nhập lại. Mượn, điều chuyển và bảo trì/sửa chữa yêu cầu `quantity=1`, nếu chưa tiếp nhận trả `409 DEVICE_NOT_RECEIVED`.
- `availability=NotReceived` lọc hồ sơ chưa tiếp nhận và chưa được giữ bởi phiếu. `Available` chỉ gồm thiết bị có `quantity=1`, chưa có operation hold. Thiết bị số lượng 0 đã được duyệt tiếp nhận có availability `Reserved`.
- Dropdown chọn thiết bị trên phiếu tiếp nhận dùng `GET /devices?availability=NotReceived&status=Active&departmentCode=...&search=...`, giữ nguyên phân trang hiện có.
- Migration bổ sung số lượng mặc định 0, backfill 1 cho thiết bị thuộc phiếu tiếp nhận đã hoàn tất, và CHECK constraint giới hạn 0/1.

## Quyền

| Thao tác | QTTB Manager | QTTB Staff | Department Staff |
|---|---:|---:|---:|
| List/detail/barcode/history | Toàn cục | Toàn cục | Chỉ thiết bị thuộc phòng ban của account |
| Tạo ngoài phiếu nhập | Có | Có | Không |
| Sửa metadata, khóa/mở khóa | Có | Có | Không |

Record ngoài phòng ban của Department Staff trả `404 DEVICE_NOT_FOUND` để tránh dò mã. Barcode được trả trong detail và barcode lookup nếu caller có scope với thiết bị. List không trả barcode cho Department Staff.

## Routes

| Method | Route | Kết quả |
|---|---|---|
| GET | `/api/v1/devices` | Paged list |
| GET | `/api/v1/devices/borrowed` | Thiết bị phòng hiện tại đang mượn, DTO chỉ đọc theo phiếu |
| GET | `/api/v1/devices/{deviceCode}` | Detail |
| GET | `/api/v1/devices/by-barcode/{barcode}` | Lookup để scan barcode |
| POST | `/api/v1/devices` | `201 Created`, detail + barcode |
| PUT | `/api/v1/devices/{deviceCode}` | `204 No Content` |
| PUT | `/api/v1/devices/{deviceCode}/lock` | Khóa hồ sơ, `204` |
| DELETE | `/api/v1/devices/{deviceCode}/lock` | Mở khóa, `204` |
| GET | `/api/v1/devices/{deviceCode}/history` | Paged audit history |
| GET | `/api/v1/history/devices` | QTTB global device timeline |

### Thiết bị phòng đang mượn

`GET /devices/borrowed` lấy thiết bị trên các phiếu có borrower department là phòng hiện tại của actor và trạng thái `OnLoan` hoặc `PendingReturnConfirmation`. Cùng quy tắc phạm vi áp dụng cho cả role QTTB đọc toàn cục: endpoint này luôn là thiết bị **phòng của actor** đang mượn, không nhận phòng mượn từ client.

Thiết bị xuất hiện khi bên mượn xác nhận thực nhận; vẫn hiển thị khi quá hạn, báo trả hoặc bên cho mượn từ chối nhận trả. Sau khi bên cho mượn xác nhận nhận lại, thiết bị không còn trong danh sách. Pending/Approved/Rejected/Cancelled/Returned không thuộc nhóm đang mượn.

Query: `search` (tên/mã/serial/phiếu/phòng cho mượn), `loanCode`, `status=OnLoan|PendingReturnConfirmation`, `overdue`, `page`, `pageSize` (tối đa 100). Scope và filter áp trước count/paging; sort dueAt, loanCode, deviceCode tăng dần.

Response gồm thông tin nhận diện/danh mục/tình trạng, `ownerDepartmentCode/Name` hiện tại, `lenderDepartmentCode/Name` trên phiếu, mã phiếu, hạn trả, thời điểm nhận/báo trả, trạng thái và cờ quá hạn. Không trả giá mua, invoice, barcode, ghi chú hoặc thông tin cá nhân người chịu trách nhiệm. Legacy đang OnLoan vẫn hiển thị; lender chưa xác định giữ null, không suy diễn từ owner hiện tại.

Trang thiết bị tách “Thiết bị phòng quản lý” và “Thiết bị đang mượn”. Nhóm mượn chỉ xem phiếu; không mở rộng quyền detail/barcode/history/sửa/khóa/điều chuyển tài sản ngoài phòng. Các thuộc tính department/responsible/area của thiết bị không thay đổi. Danh sách tài sản bên cho mượn vẫn giữ thiết bị, có trạng thái “Đang cho mượn” và metadata `activeLoanCode`, `borrowerDepartmentCode/Name`, `loanDueAt`; metadata này hết khi nhận trả xong.

### Danh sách thiết bị quản lý

`eligibleFor=Transfer|Recovery` lọc cả cây thiết bị trước count/paging: quantity=1, Active, không operation hold; Transfer yêu cầu Good và không service hold, Recovery cho Good/Damaged và cho phép service hold. Cả cây phải cùng phòng nguồn. Backend vẫn kiểm tra lại khi tạo/sửa/acquire/hoàn tất.

Mượn dùng `GET /device-loans/eligible-devices` để lấy options thiết bị phòng khác, không dùng scope danh sách quản lý thiết bị. `eligibleFor=Loan` cũ trả 400 `LOAN_ELIGIBILITY_ENDPOINT_CHANGED`. Xem [API mượn hai phòng](device-loans-api.md). Availability của thiết bị có service hold là Reserved, không Available.

Query hỗ trợ `search`, `categoryCode`, `status`, `condition`, `departmentCode`, `areaCode`, `availability`, `sortBy`, `sortDirection`, `page`, `pageSize`. `sortBy`: `DeviceCode|Name|Category|Department|Status`; direction `Asc|Desc`. `availability`: `Available|Reserved|OnLoan|TransferPending`. Mặc định page 1, pageSize 20; pageSize tối đa 100.

Response dạng `{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }`. Filter, count, sort và paging được chạy trong database. Department Staff luôn bị giới hạn vào phòng ban của account trước khi filter/paging.

### Create ngoài phiếu nhập

```json
{
  "deviceCode": "TB-2026-0001",
  "name": "Máy chiếu Epson",
  "categoryCode": "MAY_CHIEU",
  "departmentCode": "QLCSVC",
  "responsibleAccountCode": "TK_001",
  "condition": "Good",
  "serialNumber": "SN-123",
  "manufacturerCode": "EPSON",
  "model": "EB-X06",
  "purchaseDate": "2026-10-29",
  "paidInvoiceCode": "HD-2026-014",
  "purchasePrice": 12000000,
  "warrantyExpiryDate": "2028-10-29",
  "areaCode": "KV-001",
  "notes": null
}
```

`name`, `categoryCode`, `departmentCode` bắt buộc. `deviceCode` tùy chọn; khi vắng mặt server sinh mã theo format `TB` + 30 ký tự hex. Barcode luôn được sinh server-side theo format `BC` + 30 ký tự hex, duy nhất và không đổi. Category, department, area, manufacturer và người chịu trách nhiệm nếu gửi phải tồn tại và đang dùng được; người chịu trách nhiệm phải thuộc phòng ban đã chọn. Người chịu trách nhiệm có thể bỏ trống. Thiết bị bắt đầu ở `Active`; tình trạng mặc định là `Good`.

### Update metadata

```json
{
  "name": "Máy chiếu Epson EB-X06",
  "serialNumber": "SN-123",
  "manufacturerCode": "EPSON",
  "model": "EB-X06",
  "purchaseDate": "2026-10-29",
  "paidInvoiceCode": "HD-2026-014",
  "purchasePrice": 12000000,
  "warrantyExpiryDate": "2028-10-29",
  "notes": null,
  "version": "<sha256 hex, optional>"
}
```

`version` nếu gửi phải là SHA-256 64 ký tự hex của snapshot metadata đang trả về. Version cũ trả `409 VERSION_CONFLICT`. Body từ chối field không khai báo. Endpoint này chỉ sửa tên, serial, nhà sản xuất, model, ngày mua, mã hóa đơn, giá, hạn bảo hành và ghi chú. Mã thiết bị/barcode, danh mục, phòng ban, khu vực, người chịu trách nhiệm, tình trạng và trạng thái không thể sửa qua endpoint này. Thiết bị đang `Locked` không cho sửa.

### Lock / unlock

Lock ngăn thiết bị tham gia workflow mới nhưng không xóa hồ sơ. Nếu device đang có operation hold, lock trả `409 DEVICE_BUSY`. Gọi lặp lại trạng thái hiện tại là no-op thành công. Unlock đặt status về `Active`.

### Detail and history

Device history is a unified timeline of device metadata events and linked receipt, loan, and transfer events. Query parameters: `eventType` (`DEVICE|RECEIPT|LOAN|TRANSFER`), `deviceCode`, `departmentCode`, `actorAccountCode`, `fromDate`, `toDate`, `page`, and `pageSize`. Results sort by timestamp descending with stable tie-breakers. Department Staff must first pass device department scope and can only read devices currently assigned to their department.

`GET /api/v1/history/devices` uses the same query and response shape and is limited to QTTB roles. Each event includes `eventType`, `actionCode`, `businessCode`, `deviceCode`, device name, current department, actor, outcome, and timestamp. QTTB can read allowlisted audit snapshots; Department Staff receive no snapshots or changed-field details. GET requests do not create audit events.

Device response gồm business code, barcode (khi caller được phép), name, category/department/area, status, physical condition, availability suy ra từ operation hold, responsible account, manufacturer và metadata mua sắm. Không trả UUID/FK nội bộ.

History trả actor code/name, action, outcome, timestamp và reason. QTTB có thể đọc snapshot allowlist; Department Staff chỉ đọc event/tác nhân/thời điểm, không nhận JSON snapshot hoặc changed fields. GET không tạo audit event.

## Error codes

| HTTP | Code | Ý nghĩa |
|---:|---|---|
| 400 | `VALIDATION_FAILED` | Sai input, enum hoặc query |
| 403 | `DEVICE_ACCESS_FORBIDDEN` | Role không được thao tác |
| 404 | `DEVICE_NOT_FOUND` | Không tồn tại hoặc ngoài department scope |
| 409 | `DEVICE_CODE_CONFLICT` | Mã thiết bị đã dùng |
| 409 | `BARCODE_CONFLICT` | Barcode đã dùng |
| 409 | `DEVICE_LOCKED` | Không sửa thiết bị đang khóa |
| 409 | `DEVICE_BUSY` | Operation hold đang mở |
| 409 | `VERSION_CONFLICT` | Metadata đã đổi kể từ lần đọc |
| 409 | `DEVICE_OPERATION_CONFLICT` / `DEVICE_REFERENCE_CHANGED` | Xung đột persistence khác hoặc tham chiếu đổi đồng thời |

Create/update/lock/unlock ghi `DEVICE_CREATED`, `DEVICE_UPDATED`, `DEVICE_LOCKED`, `DEVICE_UNLOCKED` vào `device_history` trong cùng inventory transaction. Nếu audit hoặc persistence lỗi, thay đổi nghiệp vụ rollback.
# Quan hệ thiết bị cha – thành phần

Device response có thêm `parentDeviceCode`, `parentDeviceName` và `isComponent`. FK UUID chỉ dùng nội bộ. `GET /api/v1/devices/{deviceCode}/children` trả danh sách thành phần trực tiếp, phân trang theo các query list thiết bị. Với Department Staff, thông tin cha khác phòng ban được ẩn theo scope.

QTTB có thể gắn, đổi hoặc bỏ cha qua `PUT /api/v1/devices/{deviceCode}/parent` với body `{ "parentDeviceCode": "TB-PARENT-001" }`; gửi `null` để bỏ liên kết. Backend chặn tự tham chiếu, chu trình, khác phòng ban tại thời điểm liên kết và nhánh đang có workflow hold. Metadata thiết bị vẫn độc lập, không lan truyền.

Khóa/mở khóa node áp dụng nguyên tử cho node và mọi hậu duệ. Mỗi thiết bị được audit riêng với action code có device code để đáp ứng tính duy nhất action/request trong `device_history`.
