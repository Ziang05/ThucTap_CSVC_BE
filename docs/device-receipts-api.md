# Device Receipts API

API nhập thiết bị theo contract tại [device-workflows-contract.md](device-workflows-contract.md). Mọi route yêu cầu session bearer hiện tại. Actor lấy từ database/session; client không được chọn creator, người duyệt hoặc actor history. Không nhận upload file/ảnh; `paidInvoiceCode` chỉ là mã tham chiếu tùy chọn.

Base path: `/api/v1/device-receipts`

## Quyền

| Hành động | QTTB Manager | QTTB Staff | Department Staff |
|---|---:|---:|---:|
| List/detail/history | Toàn cục | Toàn cục | Không |
| Tạo phiếu | Không | Có | Không |
| Sửa/hủy phiếu Pending | Không | Chỉ creator | Không |
| Approve/reject toàn phiếu | Có | Không | Không |
| Complete phiếu Approved | Không | Có | Không |

## Routes

| Method | Route | Kết quả |
|---|---|---|
| GET | `/api/v1/device-receipts` | Paged list |
| POST | `/api/v1/device-receipts` | `201 Created`, receipt detail |
| GET | `/api/v1/device-receipts/{receiptCode}` | Receipt detail + item lines |
| PUT | `/api/v1/device-receipts/{receiptCode}` | Sửa header/items khi Pending, `204` |
| POST | `/api/v1/device-receipts/{receiptCode}/approve` | Manager duyệt toàn phiếu, `204` |
| POST | `/api/v1/device-receipts/{receiptCode}/reject` | Manager từ chối toàn phiếu, `204` |
| POST | `/api/v1/device-receipts/{receiptCode}/complete` | Staff nhập kho toàn bộ một lần, `204` |
| POST | `/api/v1/device-receipts/{receiptCode}/cancel` | Creator hủy trước duyệt, `204` |
| GET | `/api/v1/device-receipts/{receiptCode}/history` | Paged history |

List filter: `receiptCode`, `supplierCode`, `requestingDepartmentCode`, `createdByAccountCode`, `status`, `fromDate`, `toDate`, `page`, `pageSize`. Kết quả mặc định sort `createdAt Desc`, rồi `receiptCode Desc`; filter/count/paging chạy trong database. Page size tối đa 100.

## Tạo phiếu

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
    { "existingDeviceCode": "TB-DA-KHAI-BAO" },
    { "categoryCode": "MAY_CHIEU", "name": "TV Samsung A1", "deviceCode": null, "serialNumber": null }
  ],
  "notes": null
}
```

Supplier, ngày giao, đợt giao, phòng ban yêu cầu và ít nhất một item là bắt buộc; supplier/department/category phải tồn tại và Active. Mỗi item là một thiết bị vật lý, số lượng sau tiếp nhận là 1. Không nhận `quantity` trên item; nhập 5 chiếc cần 5 dòng/mã riêng.

- Chọn thiết bị có sẵn: gửi `existingDeviceCode`; thiết bị phải Active, có `quantity=0` và cùng phòng ban với phiếu. Backend lấy tên, loại, serial, quan hệ cha và barcode từ hồ sơ; không cần gửi lại metadata. Nếu gửi metadata khác hồ sơ thì trả 400, cần sửa tại trang thiết bị. Không gửi đồng thời `existingDeviceCode` và `deviceCode`. `deviceCode` trỏ mã hồ sơ có sẵn cũng được hỗ trợ cho client cũ.
- Tạo thiết bị mới ngay trong phiếu: gửi `categoryCode`, `name`, serial/parent tùy chọn; `deviceCode` tùy chọn. Backend sinh mã nếu để trống và sinh barcode riêng, reserve vào registry. Hồ sơ mới được tạo khi complete thành công. Tên/model giống nhau vẫn có mã riêng. Mã/barcode được giữ khi sửa phiếu và không được tái sử dụng cho thiết bị khác sau khi bỏ/hủy.
- Form tạo thiết bị đầy đủ trên giao diện dùng cùng API `POST /devices` như trang Thiết bị, sau đó gửi `existingDeviceCode` vào phiếu. Hồ sơ được lưu ngay với số lượng 0, tự thêm vào danh sách tiếp nhận; hoàn tất phiếu mới tăng lên 1 và giữ nguyên metadata. Nếu đóng/hủy phiếu, hồ sơ đã tạo vẫn nằm trong danh sách thiết bị với số lượng 0. Dropdown thiết bị có sẵn là tùy chọn, lọc theo loại và phòng ban; có thể thêm nhiều thiết bị khác mã vào cùng nhóm. Payload khai báo trực tiếp ở dòng trên vẫn được hỗ trợ cho phiếu/client cũ.
- Một phiếu có thể gồm cả thiết bị có sẵn và thiết bị mới. Thiết bị có sẵn giữ nguyên ID, mã, barcode và metadata khi tiếp nhận; số lượng chỉ đổi 0 → 1 lúc complete.
- Phiếu Pending/Rejected/Cancelled không làm tăng số lượng. Thiết bị có sẵn từ phiếu bị hủy/từ chối có thể được chọn lại trong phiếu khác. Các phiếu pending có thể cùng chọn một thiết bị, nhưng chỉ một phiếu được duyệt giữ mã đó; phiếu còn lại trả `409 DEVICE_BUSY`, hoặc `DEVICE_ALREADY_RECEIVED` nếu thiết bị đã hoàn tất tiếp nhận.

## Workflow

1. QTTB Staff tạo phiếu ở `PendingApproval`. Chỉ creator được sửa/hủy ở trạng thái này.
2. QTTB Manager approve hoặc reject toàn phiếu. Approve tạo operation hold cho mọi item trong cùng transaction; không có duyệt từng phần.
3. QTTB Staff complete phiếu Approved một lần. Item có sẵn được liên kết vào đúng device và tăng `quantity` lên 1; item mới tạo một device với `quantity=1`, department lấy từ requesting department, tình trạng `Good`, trạng thái `Active`. Mỗi thiết bị và phiếu có history. Tất cả quantity/device/item/history và giải phóng hold commit nguyên tử; lỗi audit rollback toàn bộ.
4. Phiếu Approved không thể hủy. Phiếu Completed/Rejected/Cancelled không nhận mutation. Complete lặp trả `409 INVALID_WORKFLOW_TRANSITION`.

Create/detail response gồm `receiptCode`, nhà cung cấp, phòng ban yêu cầu, status, audit metadata, `itemCount`, `version`; detail có từng item với category/name/deviceCode/barcode/code source/serial và cờ completed. `existingDeviceCode` trên item cho biết mã đã có hồ sơ tại thời điểm đọc (null nếu item mới chưa complete), giúp form sửa phiếu pending khôi phục lựa chọn có sẵn. Không trả UUID/FK.

## Error codes

| HTTP | Code | Ý nghĩa |
|---:|---|---|
| 400 | `VALIDATION_FAILED` | Thiếu/sai dữ liệu, ngày/query sai |
| 403 | `WORKFLOW_ACTION_FORBIDDEN` | Role hoặc creator không được phép |
| 404 | `RECEIPT_NOT_FOUND`, `SUPPLIER_NOT_FOUND`, `DEPARTMENT_NOT_FOUND`, `DEVICE_CATEGORY_NOT_FOUND` | Không tìm thấy record |
| 409 | `INVALID_WORKFLOW_TRANSITION` | Phiếu không ở state cho phép |
| 409 | `DEVICE_CODE_CONFLICT` | Mã thiết bị đã được reserve/dùng |
| 409 | `DEVICE_BUSY` | Một item đang được operation khác giữ |
| 409 | `DEVICE_ALREADY_RECEIVED` | Mã đã có số lượng 1, không được tiếp nhận lần nữa |
| 409 | `DEVICE_RECEIPT_DEPARTMENT_MISMATCH` | Hồ sơ có sẵn khác phòng ban của phiếu |
| 409 | `DEVICE_LOCKED` | Hồ sơ có sẵn đang khóa |
| 409 | `SUPPLIER_UNAVAILABLE`, `DEPARTMENT_UNAVAILABLE`, `DEVICE_CATEGORY_UNAVAILABLE` | Danh mục/phòng ban bị khóa |
| 409 | `DEVICE_OPERATION_CONFLICT` / `DEVICE_REFERENCE_CHANGED` | Unique/FK conflict khác |

Audit actions: `RECEIPT_CREATED`, `RECEIPT_UPDATED`, `RECEIPT_APPROVED`, `RECEIPT_REJECTED`, `RECEIPT_CANCELLED`, `RECEIPT_COMPLETED`; completion còn ghi `DEVICE_RECEIVED` cho từng device. Audit và nghiệp vụ cùng transaction; lỗi audit rollback toàn bộ thao tác.
# Quan hệ cha–con khi nhập kho

Receipt item hỗ trợ `parentDeviceCode` tùy chọn. Mã này có thể trỏ tới thiết bị đã tồn tại hoặc item khác trong cùng receipt. Khi hoàn tất phiếu, backend tạo device records và liên kết cha–con trong cùng transaction. Thiết bị mới trong cùng phiếu không được tạo quan hệ vòng.
