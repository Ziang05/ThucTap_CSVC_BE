# Mượn trả giữa hai phòng ban

Cập nhật 04/11/2026. Base `/api/v1/device-loans`, mọi endpoint dùng session bearer.

Một phiếu có phòng mượn A (từ hồ sơ actor) và phòng cho mượn B (chọn bằng `lenderDepartmentCode`), A khác B. Tất cả thiết bị, kể cả hậu duệ mở rộng từ thiết bị cha, phải do B quản lý. Mượn/trả không đổi department, responsible account hoặc area thiết bị.

## Quyền và trạng thái

`DEPARTMENT_STAFF` và `QTTB_STAFF` thao tác theo phòng hiện tại; nhân viên QTTB phải thuộc phòng đã cấu hình. Hai phòng thường không cần cấu hình QTTB để mượn với nhau. QTTB giữ quyền đọc toàn cục nhưng không quyết định thay hai bên. `QTTB_MANAGER` không được thực hiện các bước nhân viên.

List/detail/history thuộc phạm vi cả A và B. Nhân viên phòng khác nhận 404; snapshot lịch sử chi tiết tiếp tục dành cho QTTB. Actor lấy từ session.

| Trạng thái | Thao tác | Bên thực hiện | Kết quả |
| --- | --- | --- | --- |
| PendingApproval | approve/reject | Nhân viên B | Approved/Rejected |
| PendingApproval | update/cancel | Creator vẫn thuộc A | PendingApproval/Cancelled |
| Approved | handover-complete | Nhân viên A | OnLoan |
| OnLoan | return | Nhân viên A báo trả | PendingReturnConfirmation |
| PendingReturnConfirmation | confirm-return | Nhân viên B thực nhận | Returned |
| PendingReturnConfirmation | reject-return | Nhân viên B, có lý do | OnLoan |

Approve là bên cho mượn đồng ý, không phải QTTB duyệt. Khi đồng ý mới giữ operation hold toàn phiếu. Báo trả không giải phóng hold hoặc đổi condition thiết bị. B thực nhận mới cập nhật condition và release đúng hold Loan của phiếu. Từ chối nhận trả giữ hold; lịch sử giữ các lần báo trả trước.

Không gia hạn hoặc giao/trả từng phần. Nhận lần đầu chấp nhận Good/Damaged; báo trả và nhận lại chấp nhận Good/Damaged/Missing. Tình trạng A khai báo lưu riêng `declaredReturnCondition/Note`; B ghi `conditionAtReturn/returnNote`.

`status=Overdue` là projection khi quá hạn và còn OnLoan hoặc PendingReturnConfirmation. `workflowStatus` luôn là trạng thái thật để xác định thao tác. Mỗi mutation tăng version và ghi audit trong cùng transaction.

## Chọn thiết bị

`GET /eligible-devices?lenderDepartmentCode=QTTB&search=may&page=1&pageSize=20`

Lender filter tùy chọn, bỏ filter để xem các nguồn khác A. Hỗ trợ thêm `categoryCode`; count/sort/paging tại DB. Options chỉ trả mã/tên/danh mục/phòng/serial/quan hệ cha con/tình trạng, không trả giá mua hay ghi chú nội bộ. Không cấp quyền detail/barcode/history thiết bị ngoài phạm vi quản lý.

Thiết bị: quantity=1, Active, Good, đúng B, không có operation hold hoặc service hold. Cả cây phải hợp lệ và cùng B; con không hợp lệ loại cả cha khỏi options. Tối đa 200 tính cả thành phần sau mở rộng. B phải hoạt động và có nhân viên có thể xác nhận.

`GET /devices?eligibleFor=Loan` cũ trả 400 `LOAN_ELIGIBILITY_ENDPOINT_CHANGED`. Chuyển caller sang options mới. `eligibleFor=Transfer|Recovery` và scope quản lý thiết bị thông thường giữ nguyên.

## Request và routes

Tạo `POST /`:

```json
{
  "lenderDepartmentCode": "QTTB",
  "purpose": "Thực hành",
  "dueAt": "2026-11-11T17:00:00+07:00",
  "items": [{ "deviceCode": "TB-MT-001" }],
  "notes": null
}
```

`dueAt` ở tương lai. `PUT /{loanCode}` cùng body và thêm `version` hiện tại. Đổi B phải chọn lại thiết bị theo B mới.

- `GET /`, `GET /{loanCode}`, `GET /{loanCode}/history`: list/detail/history.
- `POST /{loanCode}/approve`, `/reject`, `/cancel`, `/reject-return`: `{ "version": 1, "reason": "..." }`. Reason bắt buộc với reject/reject-return.
- `POST /{loanCode}/handover-complete`, `/return`, `/confirm-return`: `{ "version": 2, "items": [{ "deviceCode": "TB-MT-001", "condition": "Good", "note": null }] }`. Phải đủ tất cả item đúng một lần.

Version sai: 409 VERSION_CONFLICT; thiếu: validation error. Không nhận borrower hoặc actor tùy ý.

List filters: `loanCode`, `borrowerAccountCode`, `departmentCode` (A), `lenderDepartmentCode` (B), `status` (persisted), `overdue`, `page`, `pageSize` (tối đa 100). Sort createdAt Desc/code Desc, scope trước count/paging.

Response giữ `departmentCode/Name` là A, thêm `lenderDepartmentCode/Name`, `workflowVersion`, `workflowStatus`, `returnRequestedByAccountCode/At` và metadata hai bên. Decision lịch sử vẫn được giữ; alias lenderConfirmedBy/At chỉ cho workflow mới.

## Giữ chỗ

Create/update và lender approve kiểm tra cả hai bảng hold. Giao/trả yêu cầu hold của chính phiếu, không có service hold xung đột. Khóa hàng và transaction chung bảo vệ cạnh tranh với BT/SC/điều chuyển. Một item hoặc audit thất bại rollback toàn bộ.

Allocation/InterDepartment cũng chặn service hold. Recovery về QTTB cho phép có service hold nhưng không chiếm operation hold khác; không xóa service hold/guard hoặc ghi kết quả BT/SC thay nguồn.

Audit bổ sung `LOAN_RETURN_REQUESTED`, `LOAN_RETURN_REJECTED`, `LOAN_WORKFLOW_MIGRATED`; giữ các action tạo/sửa/hủy/đồng ý/từ chối/giao/nhận lại hiện có. `LOAN_RETURNED` chỉ ghi khi B thực nhận.

Lỗi chính: `LOAN_SAME_DEPARTMENT`, `LOAN_LENDER_MISMATCH`, `LENDER_UNAVAILABLE`, `DEVICE_BUSY`, `DEVICE_NOT_RECEIVED`, `DEVICE_LOCKED`, `DEVICE_NOT_AVAILABLE`, `WORKFLOW_ACTION_FORBIDDEN`, `INVALID_WORKFLOW_TRANSITION`, `VERSION_CONFLICT`, `LOAN_LEGACY_REVIEW_REQUIRED`.

Migration đánh dấu phiếu cũ workflowVersion=1, không tự gán lender hoặc đổi lịch sử QTTB duyệt. Phiếu cũ vẫn đọc được và giữ hold, cần rà soát trước thao tác mới. Xem [hướng dẫn release và legacy](device-loans-release.md).

## Hiển thị thiết bị tại phòng mượn

Trang thiết bị có nhóm “Thiết bị đang mượn”, lấy từ `GET /devices/borrowed` theo borrower department hiện tại. Chỉ hiển thị từ `OnLoan`, giữ trong `PendingReturnConfirmation` và cả khi quá hạn; `Returned` mới hết hiển thị. Phòng quản lý vẫn giữ tài sản và thấy phòng đang mượn/mã phiếu/hạn trả. Quyền quản lý tài sản không chuyển sang bên mượn. Xem [Devices API](devices-api.md).
