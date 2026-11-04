# Device transfers API

Base path: `/api/v1/device-transfers`. All routes require an authenticated active session.

## Permissions and scope

- `DepartmentStaff` and `EquipmentStaff` may create only for their source department. Creating on behalf of other departments is not supported. Creators may edit only in `PendingConfirmation`, or cancel in `PendingConfirmation`/`PendingApproval`.
- Receiving-department staff accept/reject receipt intent and confirm physical receipt. These may be different staff members; neither must be the target responsible person. Sender staff cannot confirm physical receipt.
- Recovery to the configured equipment department requires `EquipmentStaff` for receiver acceptance/rejection and physical receipt. A `DepartmentStaff` account assigned to QTTB does not receive these recovery permissions. `EquipmentManager` remains a reader/manager rather than a receiving participant.
- `EquipmentStaff` and `EquipmentManager` may list/read all requests and decide only `InterDepartment` requests in `PendingApproval` after receiver acceptance. No new restriction against self-approval is introduced. `EquipmentManager` does not receive staff participant permissions.
- All workflow mutations require a configured equipment department. Equipment-role actors must belong to that department. See [equipment settings](equipment-settings-api.md) for setup and account repair.
- Out-of-scope requests return `404` so transfer codes cannot be used to discover other departments' requests.

## Routes

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/v1/device-transfers` | Filtered, paged list |
| POST | `/api/v1/device-transfers` | Create in `PendingConfirmation` |
| GET | `/api/v1/device-transfers/{transferCode}` | Detail with items |
| PUT | `/api/v1/device-transfers/{transferCode}` | Edit creator's pending request |
| POST | `/api/v1/device-transfers/{transferCode}/confirm-receipt-intent` | Receiver accepts the request |
| POST | `/api/v1/device-transfers/{transferCode}/reject-receipt-intent` | Receiver rejects; optional `{ "reason": "..." }` |
| POST | `/api/v1/device-transfers/{transferCode}/approve` | Approve all items and reserve them |
| POST | `/api/v1/device-transfers/{transferCode}/reject` | Reject the pending request |
| POST | `/api/v1/device-transfers/{transferCode}/handover-complete` | Complete handover for all items |
| POST | `/api/v1/device-transfers/{transferCode}/cancel` | Cancel before approval |
| GET | `/api/v1/device-transfers/{transferCode}/history` | Paged request history |

## Create and update body

```json
{
  "sourceDepartmentCode": "KHOA_A",
  "targetDepartmentCode": "KHOA_B",
  "targetResponsibleAccountCode": "NV000123",
  "reason": "Điều chuyển thiết bị theo thống nhất giữa hai phòng ban",
  "notes": "Ghi chú tùy chọn",
  "items": [{ "deviceCode": "TB000123" }]
}
```

The source and target must differ and be active. The responsible account must belong to the target department. The creator must be staff of the source department. The request accepts 1–200 distinct devices; each must be active, received (`quantity=1`), currently assigned to the source department, and free from another operation reservation. Allocation and inter-department transfer require `Good`; recovery accepts `Good` or `Damaged`, including all descendants. `Missing` is rejected. `PUT` replaces the request's editable values and item list before receiver acceptance and derives the transfer type again.

## Three workflows

Backend derives and stores `transferType`; clients cannot supply it. Source equals the configured equipment department: `Allocation`. Both departments differ from it: `InterDepartment`. Target equals the equipment department: `Recovery`. Only the source department creates the request, including recovery; QTTB cannot create it on behalf of another department.

- Allocation: `PendingConfirmation` → receiver acceptance → `Approved` → receiver physical receipt → `Completed`. Acceptance atomically validates/reserves all devices; QTTB decision fields remain null.
- InterDepartment: `PendingConfirmation` → receiver acceptance → `PendingApproval` → QTTB approval → `Approved` → receiver physical receipt → `Completed`. Reservations are acquired only at QTTB approval.
- Recovery: `PendingConfirmation` → QTTB staff acceptance → `Approved` → QTTB staff physical receipt → `Completed`. Acceptance atomically validates/reserves every device. There is no separate manager approval, and QTTB decision fields remain null. The receiver and the target responsible account may differ.
- Receiver rejection ends in `Rejected`, records separate receiver rejection fields and history, and does not populate QTTB decision fields. No devices are moved by acceptance or approval.
- `Approved` is displayed as “Chờ bàn giao” for transfers. Editing after acceptance is blocked; creators may cancel before `Approved`.
- Recovery is displayed as “Thu hồi về QTTB”, with “Chờ QTTB tiếp nhận”, “Chờ bàn giao” and “Đã thu hồi”. Approved recovery cannot be edited/cancelled and cannot use `/approve` or `/reject` to bypass reception.

## Approve and reject

Both routes accept an optional body:

```json
{ "reason": "Ghi chú quyết định tùy chọn" }
```

Approval validates the full device set again and reserves every device atomically. Rejection changes only the request state. Approval is all-or-nothing; an approved request cannot be cancelled or edited.

## Handover completion

```json
{
  "sourceHandoverAccountCode": "NV000123",
  "items": [
    { "deviceCode": "TB000123", "condition": "Good", "note": "Đã bàn giao" }
  ]
}
```

Submit each approved device exactly once. `condition` is `Good` or `Damaged`; `Missing` is rejected. In one database transaction, the API updates department and responsible account, clears the previous area, records the received condition and note, writes device and transfer history, and releases reservations. Partial completion is not supported. The response is `204 No Content`.

For recovery, each item must explicitly include `condition`; omitting it returns validation error instead of silently recording a damaged device as good. Detail items include `currentCondition` so the receiving form starts with the actual current condition. Other transfer types retain their previous `Good` default if condition is omitted.

Recovery uses the selected QTTB department and responsible account, not automatically the physical receiver. Completing recovery does not automatically repair damaged devices: the submitted condition is recorded and subsequent allocation/loan eligibility still excludes damaged devices. Device history records `DEVICE_RECOVERY_COMPLETED_{deviceCode}` and includes `transferType: "Recovery"`; transfer history retains the receiver and completion actions with the type in snapshots.

`sourceHandoverAccountCode` is optional information for the handover record, validated against active staff of the source department; it does not represent a separate authenticated sender confirmation. The receiver account is always the current authenticated actor. Supplying `targetHandoverAccountCode` is rejected as an unsupported field.

## List filters and response

List filters: `transferCode`, `departmentCode`, `status`, `fromDate`, `toDate`, `page`, and `pageSize` (maximum 100). Status values are `PendingConfirmation`, `PendingApproval`, `Rejected`, `Approved`, `Completed`, and `Cancelled`.

Detail/list include `transferType`, `receiverConfirmedAccountCode`, `receiverConfirmedAt`, `receiverRejectedAccountCode`, `receiverRejectedAt`, `receiverRejectionReason`, QTTB decision fields and completion fields. Audit distinguishes `TRANSFER_RECEIVER_CONFIRMED`, `TRANSFER_RECEIVER_REJECTED`, `TRANSFER_APPROVED`, `TRANSFER_REJECTED`, and `TRANSFER_COMPLETED`.

Create returns `201 Created` with a detail resource. Read/list return the standard `PagedResult`/DTO shape. Mutations return `204 No Content`. Errors use the API's standard problem response and workflow error codes, including `INVALID_WORKFLOW_TRANSITION`, `DEVICE_BUSY`, `DEVICE_NOT_IN_SOURCE_DEPARTMENT`, and `TRANSFER_NOT_FOUND`.

Department staff history responses omit raw before/after snapshots and changed-field details; QTTB roles can see them. The implementation does not store or return rendered barcode images for transfers.
# Tập thiết bị có quan hệ thành phần

Khi tạo hoặc sửa phiếu, mỗi device code được mở rộng thành thiết bị đó và mọi hậu duệ. Backend loại trùng khi các nhánh được chọn chồng nhau; toàn bộ thiết bị mở rộng phải thuộc phòng ban nguồn, nếu không trả `DEVICE_GROUP_SOURCE_MISMATCH`. Điều chuyển cập nhật phòng ban và người chịu trách nhiệm cho từng item đã mở rộng.

## Giữ chỗ cập nhật 04/11/2026

Allocation/InterDepartment kiểm tra cả device_operation_holds và service_order_device_holds tại danh sách, create/update, acquire và hoàn tất; cả cây phải hợp lệ. Recovery cho phép có service hold nhưng vẫn không được có operation hold khác. Sau đồng ý/duyệt, hoàn tất yêu cầu Transfer hold thuộc chính phiếu. Thu hồi không xóa service hold/guard hoặc ghi kết quả BT/SC.
