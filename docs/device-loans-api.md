# Device Loans API

API mượn/trả nội bộ theo [device-workflows-contract.md](device-workflows-contract.md). Mọi endpoint cần session bearer. Borrower account/department, creator, actor duyệt, người giao và người nhận lấy từ session; API không cho client chỉ định các actor này.

Base path: `/api/v1/device-loans`

## Quyền và phạm vi

| Hành động | QTTB Manager | QTTB Staff | Department Staff |
|---|---:|---:|---:|
| List/detail/history | Toàn cục | Toàn cục | Loan của phòng ban hiện tại hoặc loan do account tạo/mượn |
| Tạo/sửa/hủy Pending | Không | Không | Có; chỉ tạo cho chính account, sửa/hủy creator |
| Approve/reject | Có | Có | Không |
| Handover/return | Không | Không | Nhân viên thuộc borrower department |

Object ngoài scope trả `404 LOAN_NOT_FOUND`. Thiết bị mượn có thể thuộc đơn vị quản lý khác; loan không đổi department, area hoặc responsible account.

## Routes

| Method | Route | Kết quả |
|---|---|---|
| GET | `/api/v1/device-loans` | Paged list |
| POST | `/api/v1/device-loans` | `201 Created` + detail |
| GET | `/api/v1/device-loans/{loanCode}` | Detail + device items |
| PUT | `/api/v1/device-loans/{loanCode}` | Sửa purpose/dueAt/items khi Pending, `204` |
| POST | `/api/v1/device-loans/{loanCode}/approve` | QTTB duyệt toàn phiếu, `204` |
| POST | `/api/v1/device-loans/{loanCode}/reject` | QTTB từ chối toàn phiếu, `204` |
| POST | `/api/v1/device-loans/{loanCode}/handover-complete` | Xác nhận giao đủ toàn bộ, `204` |
| POST | `/api/v1/device-loans/{loanCode}/return` | Trả đủ toàn bộ một lần, `204` |
| POST | `/api/v1/device-loans/{loanCode}/cancel` | Creator hủy trước duyệt, `204` |
| GET | `/api/v1/device-loans/{loanCode}/history` | Paged history |

List filter: `loanCode`, `borrowerAccountCode`, `departmentCode`, `status`, `overdue`, `page`, `pageSize`. Sort mặc định `createdAt Desc`, sau đó `loanCode Desc`. Department scope được áp dụng trước filter/count/paging. Page size tối đa 100.

## Tạo và sửa phiếu

```json
{
  "purpose": "Sử dụng hội trường",
  "dueAt": "2026-11-05T17:00:00+07:00",
  "items": [{ "deviceCode": "TB-2026-0001" }],
  "notes": null
}
```

Borrower là Department Staff hiện tại. `dueAt` phải ở tương lai; backend lưu UTC. Cần 1–200 item; mã thiết bị không được trùng. Thiết bị phải tồn tại, Active, condition `Good`, và không có operation hold. Phiếu mới có mã `MT` + 30 ký tự hex và trạng thái `PendingApproval`. Creator được thay header/items hoặc cancel khi còn Pending.

## Duyệt, giao và trả

1. QTTB Manager hoặc Staff duyệt/từ chối toàn phiếu. Approve kiểm tra lại tất cả thiết bị và giữ operation hold cho toàn bộ danh sách trong cùng transaction.
2. Department Staff thuộc borrower department xác nhận đã nhận đủ. Request phải có mỗi deviceCode đúng một lần; condition mặc định `Good`, không chấp nhận `Missing`. Có thể ghi note tình trạng. Thao tác chuyển loan từ `Approved` sang `OnLoan` và ghi người xác nhận/time.
3. Khi trả, request phải có đủ mỗi deviceCode một lần và condition `Good|Damaged|Missing` cho từng item. Backend cập nhật condition gần nhất của device, đánh dấu loan `Returned` và giải phóng toàn bộ holds trong một transaction.
4. Không trả một phần, gia hạn, hoặc hủy sau duyệt. Condition lúc trả `Damaged`/`Missing` ngăn thiết bị được mượn lần nữa cho đến khi condition được xử lý.
5. `Overdue` là projection: loan `OnLoan` với `dueAt < now` được response với `status: "Overdue"`, `isOverdue: true`; không có background worker hoặc persisted overdue flag.

Response list/detail dùng `loanCode`, borrower account code/name, department code/name, due/status, người xử lý theo account code, itemCount và version. Detail thêm các device code/name/category và tình trạng lúc giao/trả. Không trả UUID/FK nội bộ.

## Error codes

| HTTP | Code | Ý nghĩa |
|---:|---|---|
| 400 | `VALIDATION_FAILED` | Input thiếu/sai, dueAt quá hạn, list trùng/thiếu item |
| 403 | `WORKFLOW_ACTION_FORBIDDEN` | Role/actor không được phép |
| 404 | `LOAN_NOT_FOUND`, `DEVICE_NOT_FOUND` | Loan/device không tồn tại hoặc loan ngoài scope |
| 409 | `INVALID_WORKFLOW_TRANSITION` | State hiện tại không cho phép hành động |
| 409 | `DEVICE_BUSY` | Thiết bị được operation khác giữ |
| 409 | `DEVICE_LOCKED` | Thiết bị Locked |
| 409 | `DEVICE_NOT_AVAILABLE` | Condition không cho phép mượn |
| 409 | `LOAN_EMPTY` | Phiếu không có item |
| 409 | `DEVICE_OPERATION_CONFLICT` / `DEVICE_REFERENCE_CHANGED` | Unique/FK conflict khác |

Audit actions: `LOAN_CREATED`, `LOAN_UPDATED`, `LOAN_CANCELLED`, `LOAN_APPROVED`, `LOAN_REJECTED`, `LOAN_HANDED_OVER`, `LOAN_RETURNED`. Audit, device condition, loan items/status và hold được lưu nguyên tử trong inventory transaction. Department Staff chỉ xem history metadata cơ bản; snapshot chỉ dành cho QTTB.
# Mượn thiết bị có thành phần

Khi tạo/sửa phiếu mượn, backend mở rộng mỗi device code đã chọn thành thiết bị đó và toàn bộ hậu duệ, loại trùng các nhánh chồng nhau rồi lưu từng item vào phiếu. Giao và trả tiếp tục yêu cầu đầy đủ item của phiếu; tình trạng ghi nhận riêng từng thiết bị.
