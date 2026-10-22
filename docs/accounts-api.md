# API quản lý tài khoản và người dùng

Phạm vi: UC-TK-01 đến UC-TK-06 của tab **Nhóm 1** trong [Danh sách nghiệp vụ](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=29721909).

## Cấu trúc

- `UseCases/Accounts/DTOs`: request riêng cho tạo, cập nhật hồ sơ, tự đổi mật khẩu, quản trị đặt lại trực tiếp, phát hành và hoàn tất mã quên mật khẩu; response danh sách/chi tiết không chứa bí mật.
- `UseCases/Accounts/Mapping`: AutoMapper profile; chỉ map trường được phép. Không reverse-map response vào entity, không map mật khẩu/token vào DTO hoặc audit.
- `UseCases/Accounts/AccountService`: quyền, validation, thay đổi dữ liệu, thu hồi phiên và chọn trường audit.
- `Infrastructure/Persistence/Repositories/AccountRepository`: join hồ sơ, vai trò, phòng ban; lọc/sắp xếp/phân trang tại PostgreSQL, sau đó `ProjectTo` DTO.
- `API/Controllers/AccountsController`: nhận HTTP, gọi use case, trả HTTP status và DTO.

## Endpoint

Tiền tố: `/api/v1/accounts`. Các endpoint bảo vệ dùng `Authorization: Bearer <opaque-session-token>`.

| HTTP | Đường dẫn | Quyền | Thành công |
| --- | --- | --- | --- |
| GET | `/` | Trưởng phòng QTTB | 200, `PagedResult<AccountListItemDto>` |
| GET | `/{id}` | Trưởng phòng hoặc chính chủ | 200, `AccountDetailDto` |
| GET | `/me` | Chính chủ | 200, `AccountDetailDto` |
| POST | `/` | Trưởng phòng | 201, `CreatedAccountDto`, header Location |
| PUT | `/{id}/profile` | Trưởng phòng hoặc chính chủ; accountCode/departmentId chỉ Trưởng phòng | 204 |
| PUT | `/me/profile` | Chính chủ; accountCode/departmentId chỉ Trưởng phòng | 204 |
| PUT | `/me/password` | Chính chủ, cần mật khẩu cũ | 204; phiên hiện tại bị thu hồi |
| PUT | `/{id}/password` | Trưởng phòng, không cần mật khẩu cũ | 204; không tạo password-reset request |
| PUT | `/{id}/lock` | Trưởng phòng | 204 |
| DELETE | `/{id}/lock` | Trưởng phòng | 204; gỡ trạng thái khóa, không xóa tài khoản |
| POST | `/password-reset-requests` | Không cần đăng nhập | 202, thông báo chung |
| POST | `/password-resets` | Mã reset còn hạn/chưa dùng | 204 |

Không có endpoint xóa cứng tài khoản hoặc endpoint ghi audit công khai.

## DTO và ví dụ

Tạo tài khoản:

```json
{
  "fullName": "Nguyễn Văn An",
  "email": "an@example.com",
  "phone": "0901234567",
  "roleId": 2,
  "departmentId": "11111111-1111-1111-1111-111111111111"
}
```

`accountCode` là trường tùy chọn khi tạo. Không gửi, null hoặc chuỗi trắng thì server cấp mã `TK000001`, `TK000002`… bằng PostgreSQL sequence; mã nhập tay được trim và viết hoa. Mã dài tối đa 32 ký tự, bắt đầu bằng chữ A–Z hoặc số; phần còn lại cho phép chữ, số, `.`, `_`, `/`, `-`. Ví dụ `NV.QTTB/2026-01`. Mã không trùng với tài khoản khác, không phân biệt hoa/thường và khoảng trắng đầu/cuối, kể cả tài khoản đã khóa.

Sequence tăng độc lập cho tài khoản và phòng ban, không reset theo năm; tối thiểu 6 chữ số, tự tăng độ dài khi vượt 999999. Nếu mã nhập tay chiếm số tự sinh, server bỏ qua số đó. Số đã cấp không thu hồi sau rollback nên có thể có khoảng trống; mã không phải số thứ tự liên tục để đếm bản ghi. Mã legacy hiện có không bị đổi tự động.

Vai trò phải là một trong ba role hiện có; phòng ban phải tồn tại và hoạt động. Mật khẩu khởi tạo bằng email chuẩn hóa theo yêu cầu nghiệp vụ, chỉ lưu bằng PasswordHasher của ASP.NET Core Identity (không sử dụng hệ thống quản lý user/role Identity).

Cập nhật mã và hồ sơ trong cùng request `PUT /api/v1/accounts/{id}/profile` hoặc `PUT /api/v1/accounts/me/profile`:

```json
{
  "fullName": "Nguyễn Văn An",
  "accountCode": "NV.QTTB/2026-01",
  "birthDate": "2000-01-15",
  "phone": "0901234567",
  "departmentId": "11111111-1111-1111-1111-111111111111"
}
```

`fullName` bắt buộc; `birthDate` và `phone` có thể xóa bằng null. `departmentId` null/không gửi giữ nguyên; chỉ Trưởng phòng được gửi giá trị này. `accountCode` bỏ qua/null/chuỗi trắng giữ nguyên mã, không sinh lại mã khi cập nhật. Chỉ Trưởng phòng QTTB được gửi mã không rỗng; role nhân viên vẫn được sửa hồ sơ chính mình khi không đổi mã. Quy tắc quyền này áp dụng cho cả hai đường dẫn profile. Trùng mã trả 409 `ACCOUNT_CODE_CONFLICT`; thiếu quyền đổi mã trả 403 `ACCOUNT_CODE_CHANGE_FORBIDDEN`; tài khoản khóa trả 409 `ACCOUNT_LOCKED`; mã không hợp lệ trả 400.

Mã và các trường hồ sơ được kiểm tra rồi commit cùng một giao dịch với audit. Bất kỳ lỗi trùng mã/quyền/lưu audit đều không cho lưu một phần dữ liệu. Cập nhật thành công trả 204; gửi lại toàn bộ dữ liệu hiện tại không tạo thêm SUCCESS audit. Snapshot before/after chứa account_code cùng các trường hồ sơ; changedFields liệt kê những trường thực sự thay đổi. Action là `PROFILE_UPDATED`, hoặc `DEPARTMENT_CHANGED` khi đổi cả phòng ban. Email, role, trạng thái khóa vẫn không được chấp nhận trong DTO cập nhật. Trường JSON không có trong request bị từ chối bằng 400. Ngày sinh tương lai bị từ chối. Đổi mã giữ nguyên ID và phiên đăng nhập; hồ sơ/phòng ban chỉ đổi khi được yêu cầu trong cùng body.

Tự đổi mật khẩu dùng `currentPassword`, `newPassword`. Trưởng phòng đặt lại trực tiếp chỉ dùng `newPassword`. Hoàn tất quên mật khẩu dùng `token`, `newPassword`. Chính sách hiện tại: mật khẩu mới dài 12–128 ký tự; mật khẩu khởi tạo theo quy định email là trường hợp riêng. DTO mật khẩu dùng class để tránh tự sinh `ToString()` chứa bí mật.

Danh sách:

```http
GET /api/v1/accounts?search=nguyễn&roleId=2&isLocked=false&sortBy=CreatedAt&sortDirection=Desc&page=1&pageSize=20
```

Lọc theo một hoặc nhiều phòng ban bằng query lặp `departmentIds`:

```http
GET /api/v1/accounts?departmentIds=11111111-1111-1111-1111-111111111111
GET /api/v1/accounts?departmentIds=11111111-1111-1111-1111-111111111111&departmentIds=22222222-2222-2222-2222-222222222222&roleId=3&isLocked=false
```

Thay UUID ví dụ bằng ID phòng ban thực tế. Nhiều ID dùng điều kiện thuộc một trong các phòng ban (OR), kết hợp AND với search/roleId/isLocked. Không gửi departmentIds thì giữ danh sách không lọc phòng ban. Tối đa 100 giá trị mỗi request; UUID sai, giá trị trống, Guid.Empty hoặc hơn 100 giá trị trả 400 VALIDATION_FAILED. ID trùng không nhân đôi tài khoản; ID hợp lệ nhưng không tồn tại trả danh sách rỗng hoặc phần kết quả của các ID còn lại. Vẫn tra cứu tài khoản thuộc phòng ban Locked để bảo toàn lịch sử. Bộ lọc chạy trong PostgreSQL trước Count/sort/Skip/Take, totalCount là số tài khoản sau lọc. Quyền lấy danh sách vẫn chỉ dành cho Trưởng phòng QTTB.

- Tìm một phần họ tên, không phân biệt hoa/thường; không tự bỏ dấu tiếng Việt.
- `sortBy`: `FullName` (mặc định) hoặc `CreatedAt`; `sortDirection`: `Asc` (mặc định) hoặc `Desc`. Luôn thêm khóa Id để ổn định phân trang.
- `page`: 1–1.000.000; `pageSize`: 1–100, mặc định 20. Tổng kết quả áp dụng cùng bộ lọc; không có kết quả trả mảng rỗng.
- `CreatedAt`/`CreatedBy` lấy từ sự kiện ACCOUNT_CREATED thành công; tài khoản bootstrap/legacy thiếu sự kiện này trả null. Không tạo cột created_at giả.
- `HasValidSession` chỉ thể hiện phiên active chưa hết hạn, không phải trạng thái online thời gian thực. Tài khoản đã khóa vẫn tra cứu được bởi Trưởng phòng.

## Giao dịch và audit

API đọc lịch sử thao tác tài khoản: `GET /api/v1/history/accounts` (danh sách gọn, có lọc/sort/phân trang), `GET /api/v1/history/accounts/{id}` (chi tiết bằng history ID, có snapshot và changedFields). Chỉ Trưởng phòng QTTB được đọc. Xem [History API](history-api.md).

`AccountTransaction` mở transaction PostgreSQL READ COMMITTED, lấy advisory lock chung cho các thao tác tài khoản và chỉ commit khi dữ liệu cùng audit được lưu thành công. Kiểm tra quyền/phiên của người thao tác được lặp lại sau khi lấy lock để từ chối request đã xác thực nhưng bị thu hồi trong khi chờ. Phòng ban đích được khóa hàng FOR SHARE trong lúc kiểm tra và ghi, tránh bị khóa/xóa giữa chừng.

Khóa tài khoản hoặc thay mật khẩu đóng phiên active và ghi SESSION_ENDED duy nhất; các yêu cầu khóa/mở khóa lặp lại không tạo SUCCESS giả. Khóa bản thân và khóa quản trị hoạt động cuối cùng bị từ chối. Lỗi use case được ghi DENIED/FAILED trong transaction mới sau rollback. Yêu cầu HTTP sai cú pháp/model bị lớp API từ chối trước khi chạy use case.

`request_id` vẫn là ID do server sinh để truy vết một lần gọi HTTP; unique audit index chống ghi trùng trong lần gọi đó. Đây chưa phải cơ chế idempotency bền vững cho nhiều HTTP request khác nhau dùng cùng Idempotency-Key.

Migration `20261022011625_EditableBusinessCodes` bổ sung hai sequence và cột generated `normalized_code` cùng unique index cho accounts/departments. Cần chạy migration trước khi dùng binary mới; dữ liệu cũ có mã trùng sau trim/đổi hoa thường phải được xử lý trước, migration không tự đổi mã để che xung đột. Lịch sử đổi mã hiện dùng audit có timestamp, chưa có lịch áp dụng mã trong tương lai.

## Xác thực phiên và phần tích hợp còn lại

Handler `DatabaseSession` băm SHA-256 token opaque (UTF-8, chuỗi hex chữ hoa), tra auth_sessions và kiểm tra active, hạn dùng, khóa tài khoản, vai trò hiện tại trong DB. Không nhận account id/role do client tự khai. Nhóm 2 đã có endpoint đăng nhập/cấp phiên, đăng xuất, bộ đếm sai và xử lý phiên hết hạn; xem [authentication-api.md](authentication-api.md). Bootstrap quản trị đầu tiên cũng là luồng riêng; không seed mật khẩu quản trị hoặc endpoint bypass.

Các thao tác ghi account/session/reset phải phối hợp cùng `AccountTransaction.AdvisoryLockKey` để bảo toàn các điều kiện nhiều hàng. Nhóm 1 và Nhóm 2 dùng chung SessionLifecycle để kết thúc phiên một lần. Handler từ chối và đóng phiên hết hạn; background worker dọn các phiên không còn request.

## Email quên mật khẩu

Cấu hình bằng environment variables hoặc user-secrets, không commit mật khẩu SMTP:

```text
PasswordReset__Host=smtp.example.com
PasswordReset__Port=587
PasswordReset__From=no-reply@example.com
PasswordReset__Username=...
PasswordReset__Password=...
PasswordReset__EnableSsl=true
```

Nếu chưa cấu hình Host/From, endpoint phát hành mã trả 503 cho mọi email. Khi đã cấu hình, email không tồn tại hoặc account khóa cũng nhận cùng 202, không công khai token. Mã gồm 32 byte ngẫu nhiên, có hạn 30 phút; DB chỉ lưu SHA-256. Yêu cầu mới đóng yêu cầu cũ; đổi/đặt lại mật khẩu hoặc khóa account cũng đóng mã chưa dùng.

SMTP gửi sau khi DB commit. Nếu gửi lỗi, hệ thống ghi lỗi vận hành không chứa email/token và vẫn trả thông báo chung; người dùng cần yêu cầu lại. Hiện chưa có outbox/hàng đợi gửi lại email bền vững.

## AutoMapper

Dùng AutoMapper 16.2.0. Có thể đặt `AUTOMAPPER_LICENSE_KEY` theo [hướng dẫn cấp phép AutoMapper](https://docs.automapper.io/en/stable/License-configuration.html); không hardcode key hoặc tắt log kiểm tra license.

## Kiểm thử

```bash
dotnet build CSVC.sln
dotnet test CSVC.sln
```

Để chạy cả integration test: đặt `CSVC_POSTGRES_TEST_CONNECTION` trỏ đến PostgreSQL UTF-8 có quyền tạo database test. Các test tự tạo/xóa database tên ngẫu nhiên, không chạy migration vào database ứng dụng. PostgreSQL cần locale hỗ trợ lower-case Unicode (ví dụ en_US.utf8) để kiểm tra tên tiếng Việt. Test bao gồm SQL projection, quyền Trưởng phòng/chính chủ, không lộ bí mật, rollback khi audit lỗi, thay thế và tiêu thụ mã một lần đồng thời, thu hồi phiên và hai quản trị khóa lẫn nhau đồng thời.
