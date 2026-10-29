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
    { "categoryCode": "MAY_CHIEU", "name": "TV Samsung A1", "deviceCode": null, "serialNumber": null }
  ],
  "notes": null
}
```

Supplier, ngày giao, đợt giao, phòng ban yêu cầu và ít nhất một item là bắt buộc; supplier/department/category phải tồn tại và Active. Mỗi item là một thiết bị vật lý. Số lượng theo loại và tổng cộng được tính từ số dòng, không nhận quantity authoritative. `deviceCode` tùy chọn; khi bỏ trống backend sinh `TB` + 30 ký tự hex. Barcode luôn được sinh `BC` + 30 ký tự hex. Cả hai được reserve toàn cục trên registry; item giữ nguyên mã/barcode khi sửa phiếu và các mã đã reserve không tái sử dụng sau khi item bị bỏ/hủy.

## Workflow

1. QTTB Staff tạo phiếu ở `PendingApproval`. Chỉ creator được sửa/hủy ở trạng thái này.
2. QTTB Manager approve hoặc reject toàn phiếu. Approve tạo operation hold cho mọi item trong cùng transaction; không có duyệt từng phần.
3. QTTB Staff complete phiếu Approved một lần. Một device được tạo cho mỗi item, department lấy từ requesting department, tình trạng `Good`, trạng thái `Active`; mỗi thiết bị và phiếu có history. Tất cả device/item/history và giải phóng hold commit nguyên tử.
4. Phiếu Approved không thể hủy. Phiếu Completed/Rejected/Cancelled không nhận mutation. Complete lặp trả `409 INVALID_WORKFLOW_TRANSITION`.

Create/detail response gồm `receiptCode`, nhà cung cấp, phòng ban yêu cầu, status, audit metadata, `itemCount`, `version`; detail có từng item với category/name/deviceCode/barcode/code source/serial và cờ completed. Không trả UUID/FK.

## Error codes

| HTTP | Code | Ý nghĩa |
|---:|---|---|
| 400 | `VALIDATION_FAILED` | Thiếu/sai dữ liệu, ngày/query sai |
| 403 | `WORKFLOW_ACTION_FORBIDDEN` | Role hoặc creator không được phép |
| 404 | `RECEIPT_NOT_FOUND`, `SUPPLIER_NOT_FOUND`, `DEPARTMENT_NOT_FOUND`, `DEVICE_CATEGORY_NOT_FOUND` | Không tìm thấy record |
| 409 | `INVALID_WORKFLOW_TRANSITION` | Phiếu không ở state cho phép |
| 409 | `DEVICE_CODE_CONFLICT` | Mã thiết bị đã được reserve/dùng |
| 409 | `DEVICE_BUSY` | Một item đang được operation khác giữ |
| 409 | `SUPPLIER_UNAVAILABLE`, `DEPARTMENT_UNAVAILABLE`, `DEVICE_CATEGORY_UNAVAILABLE` | Danh mục/phòng ban bị khóa |
| 409 | `DEVICE_OPERATION_CONFLICT` / `DEVICE_REFERENCE_CHANGED` | Unique/FK conflict khác |

Audit actions: `RECEIPT_CREATED`, `RECEIPT_UPDATED`, `RECEIPT_APPROVED`, `RECEIPT_REJECTED`, `RECEIPT_CANCELLED`, `RECEIPT_COMPLETED`; completion còn ghi `DEVICE_RECEIVED` cho từng device. Audit và nghiệp vụ cùng transaction; lỗi audit rollback toàn bộ thao tác.
