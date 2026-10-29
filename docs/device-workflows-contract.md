# Contract nghiệp vụ và API cho các luồng thiết bị

Trạng thái: **Phase 0 hoàn tất; contract baseline cho implementation**. Tài liệu này cụ thể hóa quyết định tại [backend-device-workflows-plan.md](backend-device-workflows-plan.md) và là đầu vào cho [implementation plan](backend-device-workflows-implementation-plan.md). Nếu có khác biệt với tài liệu SRS/sơ đồ cũ thì ưu tiên contract này. Các endpoint dưới đây là contract dự kiến để implement, không khẳng định API đã tồn tại.

## 1. Quy ước chung

- Base path: `/api/v1`; JSON camelCase; enum response dạng PascalCase string như `PendingApproval`, `Active`, `Good`.
- Endpoint cần xác thực dùng session bearer hiện có. Actor luôn lấy từ `ICurrentAccount`; không nhận `actorAccountId`, role hoặc quyền từ payload.
- PK/FK là UUID nội bộ. API dùng `deviceCode`, `barcode`, `receiptCode`, `loanCode`, `transferCode`, `categoryCode`, `supplierCode`, `departmentCode`.
- Thời điểm audit và thời điểm giao nhận thực tế là ISO-8601 UTC (`DateTimeOffset`). `deliveryDate`, `purchaseDate`, `warrantyExpiryDate` là ngày lịch `yyyy-MM-dd`.
- Response danh sách theo dạng `{ items, totalCount, page, pageSize }`. Mặc định `page=1`, `pageSize=20`; page 1–1.000.000, pageSize 1–100. Sort field allowlist theo từng endpoint; sort direction `Asc|Desc`, tie-breaker là business code.
- Không upload chứng từ/ảnh. Invoice/request/reference code chỉ là text. Không serialize entity, EF navigation hay arbitrary JSON ra response.
- Mutations thành công ghi audit cùng transaction. Audit thất bại thì rollback thay đổi. Không audit GET. Validation trước khi biết/đọc được target trả lỗi theo API validation/auth pipeline hiện tại.
- Error response dùng ProblemDetails hiện có, với `code` ổn định. Các code bên dưới là contract nghiệp vụ; lỗi hạ tầng không để lộ SQL/exception và trả 503.
- Version tùy chọn trên các request update metadata là SHA-256 lowercase/uppercase hex 64 ký tự trên snapshot trường có thể sửa. Version sai trả 409. State transitions vẫn được bảo vệ bằng khóa/concurrency ở DB, không dựa riêng vào version client.

## 2. Mã, độ dài và chuẩn hóa

| Giá trị | Quy tắc |
|---|---|
| `deviceCode` nhập tay | Trim, uppercase invariant; tối đa 32 ký tự; regex `^[A-Z0-9][A-Z0-9._/-]*$`; duy nhất toàn hệ thống, kể cả device Locked. Không đổi sau tạo. |
| `deviceCode` sinh | `TB` + 30 ký tự hex uppercase từ UUID ngẫu nhiên, tổng 32 ký tự. |
| `barcode` | Backend sinh sau khi device code được xác định; `BC` + 30 ký tự hex uppercase, tổng 32 ký tự. Duy nhất và bất biến. Chỉ lưu text, không lưu ảnh/file tem. Scan lookup trả về đúng device theo authorization scope. |
| Mã phiếu | Receipt `PN` + 30 hex; transfer `DC` + 30 hex; loan `MT` + 30 hex. Tổng 32 ký tự, do server sinh, bất biến. |
| `serialNumber` | Trim; tối đa 100 ký tự; tùy chọn; chưa áp unique vì serial có thể thiếu/trùng giữa hãng/model. |
| `name`, `model` | Unicode NFC, trim, gộp whitespace; name bắt buộc tối đa 200 ký tự; model tối đa 120. |
| `carrierName` | Text, tùy chọn, tối đa 200 ký tự. |
| `requestCode`, `paidInvoiceCode` | Text mã tham chiếu tùy chọn, tối đa 64 ký tự, trim; không bắt buộc phải tồn tại ở module hóa đơn bên ngoài. |
| `deliveryBatch` | Text bắt buộc, tối đa 100 ký tự (ví dụ “Đợt 1”). |
| `purpose`, `reason`, `notes` | Trim; purpose/reason tối đa 1000 ký tự; notes tối đa 4000. Chuỗi trắng thành null nếu tùy chọn. |
| Giá | Decimal(18,2), không âm, đơn vị duy nhất VND; trường tùy chọn. |
| Barcode URL | Barcode hiện dùng ký tự URL-safe; vẫn encode path parameter đúng chuẩn. |

Generated-code collision được retry tối đa 3 lần rồi trả 503. Unique constraint ở PostgreSQL là nguồn bảo vệ cuối. Nếu code/barcode được tạo ở phiếu trước khi nhập hoàn tất, giá trị đã cấp được lưu trên item và không sinh lại ở retry.

## 3. State machines

### 3.1. Device

Không trộn khóa quản trị, tình trạng vật lý và việc đang được mượn/điều chuyển:

| Trục | Giá trị | Ý nghĩa |
|---|---|---|
| `status` | `Active`, `Locked` | Khóa ngăn tham gia nghiệp vụ mới; không xóa hồ sơ/lịch sử. |
| `condition` | `Good`, `Damaged`, `Missing` | Tình trạng vật lý ghi nhận gần nhất. `Damaged`/`Missing` không được coi là sẵn sàng cho mượn/điều chuyển. |
| Operation | Không lưu thành enum trên device | Suy ra từ operation hold đang mở: `Available`, `Reserved`, `OnLoan`, `TransferPending`. Tối đa một hold cho mỗi device. |

Device tạo ngoài receipt có `status=Active`, condition mặc định `Good`, nhưng chỉ được mượn/điều chuyển sau khi có department và người chịu trách nhiệm hợp lệ. Thiết bị nhập chỉ được tạo/active khi receipt completion thành công. Khi một operation bị từ chối/hủy trước duyệt, hold được bỏ. Device lock bị từ chối nếu có operation đang giữ.

### 3.2. Receipt

Receipt tạo trực tiếp ở `PendingApproval`; không có draft riêng ở API v1.

| Hiện tại | Lệnh | Vai trò | Tiếp theo | Ghi chú |
|---|---|---|---|---|
| `PendingApproval` | Edit | Creator là QTTB Staff | `PendingApproval` | Có thể sửa header/items; giữ code đã reserve; validate lại toàn bộ. |
| `PendingApproval` | Cancel | Creator là QTTB Staff | `Cancelled` | Trước duyệt được phép hủy; giải phóng mã đã reserve theo chính sách mã (mã device đã cấp không tái sử dụng). |
| `PendingApproval` | Approve | QTTB Manager | `Approved` | Duyệt toàn phiếu; reserve toàn bộ items. |
| `PendingApproval` | Reject | QTTB Manager | `Rejected` | Từ chối toàn phiếu; yêu cầu tạo phiếu mới nếu cần thử lại. |
| `Approved` | Complete | QTTB Staff | `Completed` | Tạo device cho mọi item trong một transaction; không complete một phần. Không hủy sau duyệt. |
| Terminal state | Any mutation | Any | Không đổi | Trả 409 invalid transition. |

Completion retry sau khi hoàn tất trả 409 `RECEIPT_ALREADY_COMPLETED`; bảo vệ unique `receipt_item.device_id`/quan hệ tương đương. Không tạo device mới lần hai.

### 3.3. Loan

| Hiện tại | Lệnh | Vai trò | Tiếp theo | Ghi chú |
|---|---|---|---|---|
| `PendingApproval` | Edit/Cancel | Creator trong department của borrower | PendingApproval/`Cancelled` | Trước duyệt. |
| `PendingApproval` | Approve/Reject | QTTB Manager hoặc QTTB Staff được phân quyền | `Approved`/`Rejected` | Quyết định cho cả phiếu. Approve giữ mọi device. |
| `Approved` | HandoverComplete | Department Staff trong borrower department | `OnLoan` | Xác nhận nhận đủ toàn bộ; ghi condition/time/actor per device. |
| `OnLoan` | Return | Borrower hoặc Department Staff cùng department được phân công | `Returned` | Trả đủ tất cả items trong một lần; ghi condition/time/actor per item; giải phóng hold. |
| `OnLoan` sau due date | Read | Reader có scope | `Overdue` projection | Không persisted transition/worker; `dueAt < now` và chưa returned. |
| Terminal state | Any mutation | Any | Không đổi | Trả 409. |

Loan có một borrower nội bộ; không gia hạn và không trả từng phần. Người chịu trách nhiệm tài sản trên device không đổi khi giao/trả loan. Nếu condition lúc trả là `Damaged` hoặc `Missing`, device vẫn được ghi nhận nhưng hold kết thúc và condition ngăn dùng lại cho đến khi QTTB cập nhật qua nghiệp vụ được cấp phép.

### 3.4. Transfer

| Hiện tại | Lệnh | Vai trò | Tiếp theo | Ghi chú |
|---|---|---|---|---|
| `PendingApproval` | Edit/Cancel | Creator thuộc source hoặc target department | PendingApproval/`Cancelled` | Chỉ trước duyệt. |
| `PendingApproval` | Approve/Reject | QTTB Manager hoặc QTTB Staff được phân quyền | `Approved`/`Rejected` | Quyết định toàn phiếu; approve giữ toàn bộ device. |
| `Approved` | HandoverComplete | Department Staff thuộc source hoặc target; khai báo actor giao/nhận thuộc đúng hai bên | `Completed` | Một lần cho toàn bộ; cập nhật department/area/responsible person cùng transaction. Không hủy sau duyệt. |
| Terminal state | Any mutation | Any | Không đổi | Trả 409. |

Source và target phải khác nhau. Mỗi device phải đang do source department quản lý khi tạo và vẫn vậy khi duyệt/hoàn tất. Transfer completion đổi department và người chịu trách nhiệm tài sản sang target; loan không đổi hai thuộc tính này.

## 4. Role và phạm vi dữ liệu

| Hành động | QTTB Manager | QTTB Staff | Department Staff |
|---|---:|---:|---:|
| Đọc device/list/detail/barcode/history toàn cục | Có | Có | Không |
| Device create ngoài nhập, edit metadata, lock/unlock | Có | Có | Không |
| Receipt create/edit/cancel khi pending/complete | Không tạo theo mặc định | Có; edit/cancel creator only | Không |
| Receipt approve/reject | Có | Không | Không |
| Loan create | Có thể tạo hộ không; v1 không expose | Không | Có, borrower là account hiện tại |
| Loan approve/reject | Có | Có nếu role được cấp quyền duyệt QTTB | Không |
| Loan handover/return | Không | Không | Borrower hoặc nhân viên được phân công trong borrower department |
| Transfer create/cancel pending | Không tạo hộ theo mặc định | Không | Có nếu account thuộc source hoặc target department |
| Transfer approve/reject | Có | Có nếu role được cấp quyền duyệt QTTB | Không |
| Transfer handover complete | Không | Không | Nhân viên source hoặc target department |
| Read phiếu loan/transfer | Toàn cục | Toàn cục | Cùng department liên quan hoặc là creator/borrower |
| Read receipt/history | Toàn cục | Toàn cục | Không |

V1 mặc định cho phép cả QTTB Manager và QTTB Staff duyệt loan/transfer; riêng receipt chỉ Manager duyệt theo quyết định đã chốt. Kiểm tra account/session/role từ database trong transaction ghi. Department Staff bị giới hạn theo `departmentId` hiện tại trong account profile; không nhận department scope từ client.

Áp dụng scope trong query trước search/count/paging. Record ngoài scope trả 404 để không cho dò code; record nhìn thấy nhưng action không được phép trả 403. Tất cả query theo mã route phải xác nhận record nằm trong scope trước khi trả detail/history.

## 5. DTO và endpoint contracts

### 5.1. Devices

| Method / route | Quyền | Kết quả |
|---|---|---|
| GET `/devices` | QTTB toàn cục; Department Staff trong department mình | 200 paged |
| GET `/devices/{deviceCode}` | Theo device scope | 200; 404 ngoài scope |
| GET `/devices/by-barcode/{barcode}` | Theo device scope | 200; 404 code không tồn tại/ngoài scope |
| POST `/devices` | QTTB Manager/Staff | 201 + Location |
| PUT `/devices/{deviceCode}` | QTTB Manager/Staff | 204 |
| PUT `/devices/{deviceCode}/lock` | QTTB Manager/Staff | 204 |
| DELETE `/devices/{deviceCode}/lock` | QTTB Manager/Staff | 204 |
| GET `/devices/{deviceCode}/history` | QTTB toàn cục; Department Staff trong scope | 200 paged |

Create ngoài nhập request:

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
  "areaCode": "TOA-A-PHONG-01",
  "notes": null
}
```

`deviceCode`, `responsibleAccountCode`, serial/manufacturer/model/date/invoice/price/warranty/area/notes có thể null khi hợp lý. Name/category/department bắt buộc; nếu responsible account null thì device được giữ nhưng chưa cho mượn/điều chuyển. Barcode luôn do backend sinh và trả về, không nhận từ request create ngoài nhập.

General update chỉ nhận `name`, `serialNumber`, `manufacturerCode`, `model`, `purchaseDate`, `paidInvoiceCode`, `purchasePrice`, `warrantyExpiryDate`, `notes`, `version`. Category chỉ được đổi trước giao dịch đầu tiên, qua một operation riêng hoặc DTO update được xác thực riêng. Không cho đổi code, barcode, source, department, area, responsible person, condition/status/workflow state bằng general update. Department/area/responsible thay đổi chỉ qua transfer; condition thay đổi qua handover/return hoặc nghiệp vụ QTTB được cấp quyền.

List query: `search`, `categoryCode`, `status`, `condition`, `departmentCode`, `areaCode`, `availability`, `sortBy`, `sortDirection`, `page`, `pageSize`. `sortBy`: `DeviceCode|Name|Category|Department|Status`; mặc định `DeviceCode Asc`. `availability`: `Available|Reserved|OnLoan|TransferPending`; lọc theo operation state tính tại query.

### 5.2. Receipts

| Method / route | Quyền | Thành công |
|---|---|---|
| GET/POST `/device-receipts` | QTTB Manager/Staff đọc; QTTB Staff tạo | 200 paged / 201 |
| GET `/device-receipts/{receiptCode}` | QTTB Manager/Staff | 200 |
| PUT `/device-receipts/{receiptCode}` | Creator QTTB Staff, PendingApproval | 204 |
| POST `/device-receipts/{receiptCode}/approve` | QTTB Manager | 204 |
| POST `/device-receipts/{receiptCode}/reject` | QTTB Manager | 204 |
| POST `/device-receipts/{receiptCode}/complete` | QTTB Staff, Approved | 204 |
| POST `/device-receipts/{receiptCode}/cancel` | Creator QTTB Staff, PendingApproval | 204 |
| GET `/device-receipts/{receiptCode}/history` | QTTB Manager/Staff | 200 paged |

Create request:

```json
{
  "supplierCode": "NCC_ABC",
  "carrierName": "Giao Hàng Nhanh",
  "requestCode": "YC-2026-014",
  "deliveryDate": "2026-10-29",
  "deliveryBatch": "Đợt 1",
  "requestingDepartmentCode": "QLCSVC",
  "paidInvoiceCode": "HD-2026-014",
  "items": [
    { "categoryCode": "MAY_CHIEU", "name": "TV Samsung A1", "deviceCode": null, "serialNumber": null }
  ],
  "notes": null
}
```

Supplier, deliveryDate, deliveryBatch, requestingDepartment và ít nhất một item bắt buộc. Carrier/request code/invoice code/notes tùy chọn. Mỗi item là một thiết bị vật lý; item có `categoryCode`, `name`, `deviceCode` optional, serial optional. Barcode không cần client gửi: sinh sau khi backend có device code. Response/detail trả item code và barcode đã reserve, số lượng tự đếm theo category và tổng item count. Edit chỉ khi PendingApproval; toàn bộ items được validate lại.

Approve/reject request có `{ "reason": "..." }` tùy chọn, reason tối đa 1000 ký tự; response 204. Complete không nhận device payload: tất cả metadata đã ở receipt items; transaction tạo một device per item, nhận department theo `requestingDepartment`, condition `Good`, status `Active`, responsible person null nếu chưa được khai báo qua contract sau này.

### 5.3. Loans

| Method / route | Quyền | Thành công |
|---|---|---|
| GET/POST `/device-loans` | QTTB đọc toàn cục; Department Staff trong scope tạo/đọc | 200 paged / 201 |
| GET `/device-loans/{loanCode}` | QTTB hoặc department scope | 200 |
| PUT `/device-loans/{loanCode}` | Creator, PendingApproval | 204 |
| POST `/device-loans/{loanCode}/approve` | QTTB Manager/Staff được cấp quyền | 204 |
| POST `/device-loans/{loanCode}/reject` | QTTB Manager/Staff được cấp quyền | 204 |
| POST `/device-loans/{loanCode}/handover-complete` | Department Staff borrower department | 204 |
| POST `/device-loans/{loanCode}/return` | Borrower/designated department staff | 204 |
| POST `/device-loans/{loanCode}/cancel` | Creator, PendingApproval | 204 |
| GET `/device-loans/{loanCode}/history` | Theo loan scope | 200 paged |

Create request:

```json
{
  "purpose": "Sử dụng hội trường",
  "dueAt": "2026-11-05T17:00:00+07:00",
  "items": [ { "deviceCode": "TB-2026-0001" } ],
  "notes": null
}
```

Borrower account/department lấy từ session; client không được chọn borrower khác hoặc department. DueAt phải sau thời điểm tạo và được lưu UTC. Ít nhất một device; item trùng bị từ chối. Handover request có condition cho từng item (default `Good`) và optional note. Return request phải chứa condition + note tùy chọn cho mọi item; một request cover toàn bộ danh sách. Người giao/nhận actor code lấy server-side; snapshot tên chỉ dùng nếu actor/profile lịch sử cần bảo toàn.

### 5.4. Transfers

| Method / route | Quyền | Thành công |
|---|---|---|
| GET/POST `/device-transfers` | QTTB đọc toàn cục; Department Staff source/target tạo/đọc | 200 paged / 201 |
| GET `/device-transfers/{transferCode}` | QTTB hoặc source/target department | 200 |
| PUT `/device-transfers/{transferCode}` | Creator, PendingApproval | 204 |
| POST `/device-transfers/{transferCode}/approve` | QTTB Manager/Staff được cấp quyền | 204 |
| POST `/device-transfers/{transferCode}/reject` | QTTB Manager/Staff được cấp quyền | 204 |
| POST `/device-transfers/{transferCode}/handover-complete` | Department Staff source/target | 204 |
| POST `/device-transfers/{transferCode}/cancel` | Creator, PendingApproval | 204 |
| GET `/device-transfers/{transferCode}/history` | Theo transfer scope | 200 paged |

Create request:

```json
{
  "sourceDepartmentCode": "PHONG_A",
  "targetDepartmentCode": "PHONG_B",
  "targetResponsibleAccountCode": "TK_B01",
  "reason": "Hai phòng ban đã thống nhất",
  "items": [ { "deviceCode": "TB-2026-0001" } ],
  "notes": null
}
```

Source/target bắt buộc và khác nhau; creator phải thuộc một trong hai phòng. Target responsible account bắt buộc và phải thuộc target department. Device list không rỗng, không duplicate; từng device đang ở source department. Handover request có timestamp thực tế, danh sách thiết bị giao đủ, người giao thuộc source department và người nhận thuộc target department. Complete chuyển department/area (area có thể null nếu vị trí chưa khai báo) và responsible account theo header.

## 6. List, detail và history DTOs

- Device list: `deviceCode`, `barcode` chỉ cho QTTB (Department Staff chỉ nhận barcode khi được phép scan/đọc device trong scope), `name`, `categoryCode`, `categoryName`, `departmentCode`, `departmentName`, `areaCode`, `status`, `condition`, `availability`.
- Receipt list: `receiptCode`, `supplierCode`, `supplierName`, `deliveryDate`, `deliveryBatch`, `requestingDepartmentCode`, `status`, `itemCount`, `createdAt`, `creatorAccountCode`.
- Loan list: `loanCode`, borrower account code/name theo scope, department code/name, `dueAt`, `status` (Overdue projection được phép), itemCount, createdAt.
- Transfer list: `transferCode`, source/target department code/name, status, itemCount, createdAt, creator account code.
- Detail thêm thông tin nghiệp vụ header, item states/condition, thời gian và người xử lý. Public DTO không trả UUID/FK IDs, credentials, raw audit JSON hoặc dữ liệu phòng ban ngoài scope.
- History list: event id, business code, actionCode, outcome, occurredAt, actor account code/name, item/device code nếu có. Detail thêm reasonCode, changedFields, beforeData/afterData đã lọc. RequestId chỉ hiển thị cho QTTB Manager nếu cần vận hành; không hiển thị Department Staff.

## 7. Error codes và HTTP mapping

| HTTP | Code | Tình huống |
|---:|---|---|
| 400 | `VALIDATION_FAILED` | Field thiếu/sai, mã sai format, list rỗng/trùng, due date sai, query ngoài giới hạn/field không hỗ trợ. |
| 401 | Authentication challenge hiện có | Thiếu/expired/revoked session hoặc account locked. |
| 403 | `DEVICE_ACCESS_FORBIDDEN` / `WORKFLOW_ACTION_FORBIDDEN` | Role hoặc thao tác bị cấm trên record người dùng được phép nhìn thấy. |
| 404 | `DEVICE_NOT_FOUND`, `RECEIPT_NOT_FOUND`, `LOAN_NOT_FOUND`, `TRANSFER_NOT_FOUND` | Không tồn tại hoặc nằm ngoài object scope. Cùng mã công khai để tránh enumeration. |
| 409 | `DEVICE_CODE_CONFLICT`, `BARCODE_CONFLICT`, `DEVICE_BUSY`, `DEVICE_LOCKED`, `DEVICE_NOT_IN_SOURCE_DEPARTMENT`, `INVALID_WORKFLOW_TRANSITION`, `VERSION_CONFLICT`, `RECEIPT_ALREADY_COMPLETED` | Duplicate, trạng thái thay đổi, điều kiện cạnh tranh, version cũ hoặc thao tác lặp. |
| 503 | `DEVICE_WORKFLOW_UNAVAILABLE` | Database/transaction/audit/code generation không khả dụng. Không tiết lộ nội bộ. |

Validation response không phản chiếu toàn bộ payload. Mỗi lỗi item dùng field path (ví dụ `items[1].deviceCode`) để UI chỉ đúng dòng. Concurrency conflict trả 409, không tự retry thay đổi nghiệp vụ trong service.

## 8. Audit contract

Mọi event có: server `requestId`, nullable actor account FK, `actionCode`, `outcome` (`Success|Failed|Denied|NoChange`), safe `reasonCode`, `occurredAt` UTC, target code/ID nội bộ, snapshots allowlisted. RequestId không phải idempotency key do client cung cấp. Event thành công commit cùng nghiệp vụ; event lỗi/từ chối ghi riêng sau rollback khi policy yêu cầu. Không tạo Success cho read/no-op.

| Aggregate | Action codes |
|---|---|
| Device | `DEVICE_CREATED`, `DEVICE_UPDATED`, `DEVICE_LOCKED`, `DEVICE_UNLOCKED`, `DEVICE_DELETE_DENIED` |
| Receipt | `RECEIPT_CREATED`, `RECEIPT_UPDATED`, `RECEIPT_CANCELLED`, `RECEIPT_APPROVED`, `RECEIPT_REJECTED`, `RECEIPT_COMPLETED` |
| Loan | `LOAN_CREATED`, `LOAN_UPDATED`, `LOAN_CANCELLED`, `LOAN_APPROVED`, `LOAN_REJECTED`, `LOAN_HANDED_OVER`, `LOAN_RETURNED` |
| Transfer | `TRANSFER_CREATED`, `TRANSFER_UPDATED`, `TRANSFER_CANCELLED`, `TRANSFER_APPROVED`, `TRANSFER_REJECTED`, `TRANSFER_COMPLETED` |

Snapshots:

- Device: `device_code`, `barcode`, `name`, `category_code`, `department_code`, `area_code`, `responsible_account_code`, `status`, `condition`, allowed descriptive fields.
- Receipt: mã phiếu, supplier code/name snapshot, carrier/reference/date/batch/request department, status, item codes/category/name/barcode, invoice reference; không dump EF object.
- Loan: loan code, borrower code/name snapshot, department, dueAt, item device codes, condition and transition metadata.
- Transfer: transfer code, source/target department, responsible account code, item device codes, condition/location changes.
- Không lưu password/hash/session token, arbitrary request JSON, IP/user-agent cho workflow thiết bị, hoặc thông tin liên hệ không phục vụ nghiệp vụ.

## 9. Handoff sang Phase 1

Phase 1 có thể implement dựa trên contract này. State hold dùng một open-operation invariant; các endpoint đề cập `QTTB Staff được cấp quyền` nên policy mặc định v1 là cả Manager và Staff được duyệt loan/transfer. Receipt approval giữ riêng cho Manager. Các trạng thái/count projection không được tạo schema dư nếu có thể suy ra an toàn từ operation/workflow state.
