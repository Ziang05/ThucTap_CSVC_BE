# Cấu hình phòng phụ trách thiết bị

Phòng QTTB được chọn từ phòng ban hiện hữu, lưu bằng FK `EquipmentDepartmentId`. Tên phòng và role người lập không dùng để phân loại phiếu. Không seed phòng QTTB mặc định.

## API

| Method | Route | Quyền |
| --- | --- | --- |
| GET | `/api/v1/settings/equipment-department` | `QTTB_MANAGER` |
| PUT | `/api/v1/settings/equipment-department` | `QTTB_MANAGER` |
| GET | `/api/v1/settings/workflow-context` | Ba role đang được hỗ trợ |

GET cấu hình trả `isConfigured`, `departmentCode`, `departmentName`, `version`, `updatedAt` và `mismatchedAccounts`. Mỗi tài khoản lệch phòng có `accountId`, `accountCode`, `fullName`, `roleCode`, `departmentName`; chỉ liệt kê tài khoản QTTB chưa khóa. Chưa cấu hình trả `200`, `isConfigured=false`, version ban đầu bằng 0.

PUT nhận:

```json
{ "departmentCode": "CSVC", "version": 0 }
```

Phải chọn phòng đang hoạt động. Backend kiểm tra version, tăng version khi thay đổi và ghi lịch sử cùng transaction. Lưu lại cùng lựa chọn với version đúng không tạo lịch sử mới. Thành công trả `204`; không tự đổi role hoặc phòng ban của tài khoản.

Không được đổi lựa chọn hiện có khi còn phiếu điều chuyển ở `PendingConfirmation`, `PendingApproval` hoặc `Approved`. Thiết lập lần đầu vẫn được phép. Không được khóa phòng đang được cấu hình; xóa phòng vẫn tuân theo quy tắc cấm xóa phòng của dự án.

Quy tắc chặn đổi cấu hình áp dụng cho cả `Allocation`, `InterDepartment` và `Recovery`. Nhánh thu hồi đưa thiết bị từ phòng khác về đúng phòng được cấu hình; chỉ QTTB staff đúng phòng được đồng ý tiếp nhận và xác nhận thực nhận, không mở quyền nhận cho manager hoặc nhân viên phòng ban thông thường.

GET workflow context trả `isConfigured`, `equipmentDepartmentCode`, `equipmentAccountMatchesDepartment`, `actorDepartmentId`, `actorDepartmentCode`, đủ cho UI nghiệp vụ kiểm tra cấu hình, lọc phòng nhận và dùng phòng hiện tại từ backend ngay sau khi sửa hồ sơ. Không trả danh sách tài khoản hoặc quyền ghi cấu hình.

### Tích hợp frontend nghiệp vụ

Trang thu hồi, điều chuyển và mượn/trả dùng `GET /api/v1/settings/workflow-context` để kiểm tra cấu hình cho tài khoản đang đăng nhập. Chỉ trang quản lý cấu hình của manager dùng `GET /api/v1/settings/equipment-department`; staff gọi endpoint này sẽ nhận `403 SETTINGS_ACTION_FORBIDDEN` dù phòng QTTB đã được lưu.

Với trang thu hồi, dùng `equipmentDepartmentCode` từ workflow context để tìm phòng QTTB và danh sách người phụ trách, đồng thời loại phòng này khỏi lựa chọn phòng nguồn. Chỉ bật lập phiếu cho role QTTB khi đọc context thành công, `isConfigured=true` và `equipmentAccountMatchesDepartment=true`. Hiển thị riêng trạng thái đang tải, lỗi đọc API, chưa cấu hình và tài khoản lệch phòng; lỗi `403` hoặc lỗi mạng không đồng nghĩa với chưa cấu hình.

Cache workflow context cần gắn với tài khoản hiện tại và được làm mới sau khi lưu cấu hình. Trang nghiệp vụ phải hoạt động cả khi staff tải lại trang trực tiếp, không phụ thuộc cache cấu hình còn lại từ phiên manager.

## Quyền tài khoản và thiết lập lần đầu

1. Đăng nhập bằng manager, tạo/chọn phòng ban tại trang **Cấu hình** (`/settings`).
2. Nếu tài khoản QTTB đang thuộc phòng khác, cập nhật hồ sơ tại trang **Tài khoản**. Trang Cấu hình hiển thị danh sách cần sửa.
3. Thực hiện điều chuyển và mượn trả sau khi cấu hình hợp lệ.

Tạo tài khoản QTTB hoặc đổi phòng ban của tài khoản QTTB phải khớp cấu hình nếu đã có lựa chọn. Trước khi cấu hình vẫn cho phép tạo tài khoản để thiết lập hệ thống. Quyền đăng nhập, cấu hình và quản trị tài khoản cần thiết để sửa dữ liệu không yêu cầu manager đang thuộc đúng phòng cấu hình.

Các thao tác ghi của luồng mượn/trả và điều chuyển kiểm tra cấu hình hiện hành. Tài khoản `QTTB_STAFF`/`QTTB_MANAGER` phải thuộc phòng được chọn; `DEPARTMENT_STAFF` tiếp tục thao tác trong phạm vi phòng mình. Không tự mở quyền duyệt cho người mang role nhân viên phòng ban dù họ thuộc phòng QTTB. Quyền đọc toàn cục của role QTTB được giữ nguyên. Các module khác không bổ sung kiểm tra thành viên trong đợt này.

## Lỗi nghiệp vụ

Từ 05/11/2026, quy tắc không đổi QTTB khi còn transfer mở cũng áp dụng cho phiếu Recovery do QTTB khởi tạo ở cả ba giai đoạn: chờ nguồn đồng ý, chờ nguồn bàn giao và chờ QTTB thực nhận. Các phiếu này dùng cùng `device_transfers` nên tiếp tục được chặn bởi kiểm tra hiện hữu. QTTB manager/staff được lập phiếu mới khi thuộc đúng phòng cấu hình; chỉ QTTB staff xác nhận thực nhận.

| Code | Ý nghĩa |
| --- | --- |
| `EQUIPMENT_DEPARTMENT_NOT_CONFIGURED` | Chưa chọn phòng QTTB; thao tác nghiệp vụ trả 409 |
| `EQUIPMENT_DEPARTMENT_UNAVAILABLE` | Phòng cấu hình không còn hoạt động; 409 |
| `EQUIPMENT_ACCOUNT_DEPARTMENT_MISMATCH` | Tài khoản QTTB lệch phòng; 403 |
| `EQUIPMENT_DEPARTMENT_HAS_OPEN_TRANSFERS` | Còn phiếu điều chuyển đang xử lý; 409 |
| `EQUIPMENT_DEPARTMENT_LOCK_FORBIDDEN` | Không được khóa phòng đang được cấu hình; 409 |
| `VERSION_CONFLICT` | Version không còn đúng; 409 |
| `SETTINGS_ACTION_FORBIDDEN` | Không có quyền quản lý cấu hình; 403 |

Cấu hình tồn tại sau restart ứng dụng. Reset DB sẽ mất lựa chọn; sau migration và khởi tạo tài khoản/phòng ban, chọn lại tại trang Cấu hình. Dữ liệu hiện có là dữ liệu mẫu, không có cơ chế chuyển đổi phiếu cũ sang luồng mới.

## Kiểm tra bằng Docker

```powershell
docker compose --profile tools run --rm tooling build CSVC.sln
docker compose --profile tools run --rm tooling test CSVC.sln
```

Service `tooling` dùng SDK .NET 10 và PostgreSQL trong cùng mạng Compose. Test PostgreSQL tự tạo/xóa database test riêng, không thay đổi database ứng dụng. Sau khi sửa backend, build image và chạy migration theo README.
