# Device transfers API

Base path: `/api/v1/device-transfers`. All routes require an authenticated active session.

## Permissions and scope

- `DepartmentStaff` may create a request if their department is the source or target, edit/cancel their own pending request, and list/read requests involving their department.
- `EquipmentStaff` and `EquipmentManager` may list/read all requests and approve or reject a pending request.
- Handover completion is performed by a `DepartmentStaff` member from either participating department. The actor must be one of the declared source/target handover accounts; both accounts must be active staff in their respective departments.
- Out-of-scope requests return `404` so transfer codes cannot be used to discover other departments' requests.

## Routes

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/v1/device-transfers` | Filtered, paged list |
| POST | `/api/v1/device-transfers` | Create a pending request |
| GET | `/api/v1/device-transfers/{transferCode}` | Detail with items |
| PUT | `/api/v1/device-transfers/{transferCode}` | Edit creator's pending request |
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

The source and target must differ and be active. The responsible account must belong to the target department. The creator must be staff of one of the two departments. The request accepts 1–200 distinct devices; each must be active, in good condition, currently assigned to the source department, and free from another operation reservation. `PUT` replaces the request's editable values and item list.

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
  "targetHandoverAccountCode": "NV000456",
  "items": [
    { "deviceCode": "TB000123", "condition": "Good", "note": "Đã bàn giao" }
  ]
}
```

Submit each approved device exactly once. `condition` is `Good` or `Damaged`; `Missing` is rejected. In one database transaction, the API updates department and responsible account, clears the previous area, records the received condition and note, writes device and transfer history, and releases reservations. Partial completion is not supported. The response is `204 No Content`.

## List filters and response

List filters: `transferCode`, `departmentCode`, `status`, `fromDate`, `toDate`, `page`, and `pageSize` (maximum 100). Status values are `PendingApproval`, `Rejected`, `Approved`, `Completed`, and `Cancelled`.

Create returns `201 Created` with a detail resource. Read/list return the standard `PagedResult`/DTO shape. Mutations return `204 No Content`. Errors use the API's standard problem response and workflow error codes, including `INVALID_WORKFLOW_TRANSITION`, `DEVICE_BUSY`, `DEVICE_NOT_IN_SOURCE_DEPARTMENT`, and `TRANSFER_NOT_FOUND`.

Department staff history responses omit raw before/after snapshots and changed-field details; QTTB roles can see them. The implementation does not store or return rendered barcode images for transfers.
# Tập thiết bị có quan hệ thành phần

Khi tạo hoặc sửa phiếu, mỗi device code được mở rộng thành thiết bị đó và mọi hậu duệ. Backend loại trùng khi các nhánh được chọn chồng nhau; toàn bộ thiết bị mở rộng phải thuộc phòng ban nguồn, nếu không trả `DEVICE_GROUP_SOURCE_MISMATCH`. Điều chuyển cập nhật phòng ban và người chịu trách nhiệm cho từng item đã mở rộng.
