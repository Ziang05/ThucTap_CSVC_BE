# API xác thực tài khoản — Nhóm 2

Phạm vi: UC-XT-01, UC-XT-02 và UC-XT-03 trong [tab Nhóm 2](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=1207720194). API dùng tài khoản đã tồn tại và phiên opaque lưu trong DB, không dùng JWT/refresh token. Không có endpoint đăng ký công khai hoặc ghi/sửa/xóa lịch sử xác thực.

## Đăng nhập — UC-XT-01

```http
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "staff@example.com",
  "password": "your-existing-password"
}
```

Cả ba vai trò đều được đăng nhập. Email được trim và chuẩn hóa chữ thường; mật khẩu giữ nguyên. DTO từ chối các trường ngoài hợp đồng như roleId/accountId. Email tối đa 320 ký tự, mật khẩu hiện có tối đa 1024 ký tự (không áp chính sách đặt mật khẩu mới lên mật khẩu đăng nhập).

Thành công trả 200:

```json
{
  "accessToken": "<opaque-session-token>",
  "tokenType": "Bearer",
  "expiresAt": "2026-10-21T20:00:00+00:00",
  "user": {
    "id": "11111111-1111-1111-1111-111111111111",
    "accountCode": "TK...",
    "fullName": "Nguyễn Văn An",
    "email": "staff@example.com",
    "roleId": 2,
    "roleCode": "QTTB_STAFF",
    "roleName": "Nhân viên phòng QTTB",
    "departmentId": "22222222-2222-2222-2222-222222222222",
    "departmentName": "Phòng QTTB"
  }
}
```

Token có 32 byte ngẫu nhiên, biểu diễn hex 64 ký tự; DB chỉ lưu SHA-256 của token UTF-8 ở dạng hex chữ hoa. Token thô chỉ trả trong response đăng nhập thành công, không lưu vào audit/log. Response có Cache-Control no-store. Các request bảo vệ gửi:

```http
Authorization: Bearer <accessToken>
```

Một tài khoản chỉ có một phiên active. Đăng nhập mới thành công kết thúc phiên cũ trước khi tạo phiên mới, kể cả hàng active cũ đã hết hạn nhưng chưa được dọn. Thao tác đóng phiên cũ, tạo phiên mới, xóa chuỗi sai và ghi LOGIN_SUCCESS/SESSION_CREATED cùng một transaction. Nếu audit thất bại, transaction rollback và không trả token mới; phiên cũ chưa hết hạn vẫn giữ hiệu lực.

## Đăng nhập thất bại và khóa tạm

| Trường hợp | Phản hồi công khai | Xử lý trong DB |
| --- | --- | --- |
| Email không tồn tại / mật khẩu sai | 401 INVALID_CREDENTIALS | Ghi LOGIN_FAILED; tăng bộ đếm theo email chuẩn hóa, kể cả email chưa tồn tại |
| Đủ 5 lần sai trong 10 phút | Cùng 401 như trên | Thêm LOGIN_TEMP_BLOCKED, blocked_until = thời điểm lần sai thứ 5 + 15 phút |
| Đang bị chặn tạm | Cùng 401 như trên | Ghi lần từ chối; không kéo dài hạn chặn hoặc tăng thêm bộ đếm |
| Tài khoản bị quản trị khóa | Cùng 401 như trên | Ghi lần từ chối; không tự mở khóa accounts.is_lock |
| Không lưu được kết quả/audit | 503 AUTHENTICATION_UNAVAILABLE | Rollback, ghi lỗi vận hành an toàn; không chuyển thành đăng nhập thành công |

Body lỗi thông thường luôn dùng code INVALID_CREDENTIALS và thông báo `Unable to sign in with these credentials.`; không trả lý do nội bộ, trạng thái chặn hay dấu hiệu email có tồn tại. Dữ liệu HTTP sai định dạng/model trả 400 trước khi chạy nghiệp vụ.

Cửa sổ 10 phút tính từ lần sai đầu của chuỗi; đến đúng ranh giới 10 phút, lần sai kế tiếp bắt đầu chuỗi mới. Đăng nhập thành công xóa chuỗi sai. Khi hết 15 phút, chặn tạm kết thúc nhưng khóa quản trị vẫn giữ nguyên. Các thay đổi bộ đếm được commit trước khi trả 401 để không mất dữ liệu do exception rollback. Xác minh mật khẩu dùng dummy hash cho email không tồn tại để giữ cùng loại công việc băm.

## Đăng xuất — UC-XT-02

```http
POST /api/v1/auth/logout
Authorization: Bearer <accessToken>
```

Trả 204 và chỉ kết thúc phiên của đúng token được gửi. Endpoint chấp nhận cả phiên còn hiệu lực, hết hạn, đã thu hồi, token không biết hoặc không có token: các trường hợp sau không tạo tác dụng phụ hay sự kiện trùng. Điều này cho phép client luôn đưa UI về trạng thái chưa đăng nhập khi đăng xuất. Không nhận sessionId/accountId do client khai để kết thúc phiên khác.

Chỉ thao tác đầu tiên chuyển active sang false được ghi SESSION_ENDED; gọi lại không sửa timestamp hoặc sinh bản ghi thứ hai. Token đã kết thúc bị từ chối trên API bảo vệ bằng 401. Swagger hỗ trợ bearer tùy chọn cho logout và không yêu cầu token khi login.

## Lịch sử xác thực — UC-XT-03

Use case nội bộ, không phải một controller audit công khai:

- LOGIN_SUCCESS / LOGIN_FAILED / LOGIN_TEMP_BLOCKED / SESSION_CREATED / SESSION_ENDED.
- Có request_id, email đã chuẩn hóa khi đăng nhập, account_id nếu tra được, session_id khi liên quan, timestamp UTC, outcome và mã lý do an toàn.
- Email không tồn tại vẫn có audit với account_id null. IP lấy từ kết nối server; không tin X-Forwarded-For tự gửi. User-Agent giới hạn 512 ký tự và lọc ký tự điều khiển.
- Không lưu mật khẩu, password hash hoặc token thô. SESSION_ENDED không lưu lý do kết thúc riêng.
- SessionLifecycle dùng chung giữa login, logout, khóa tài khoản, thay mật khẩu và expiration. Các writer cùng dùng advisory lock của AccountTransaction và unique index SESSION_ENDED để giữ sự kiện một lần khi chạy đồng thời.

## Phiên hết hạn và cấu hình

Mặc định phiên có hạn tuyệt đối 8 giờ, không gia hạn trượt hoặc refresh token. Thời hạn này là mặc định kỹ thuật vì sheet chưa chỉ định giá trị. Có thể cấu hình trong appsettings hoặc environment:

```text
Authentication__SessionLifetimeMinutes=480
Authentication__ExpirationEnabled=true
Authentication__ExpirationIntervalSeconds=60
Authentication__ExpirationBatchSize=100
```

Giới hạn hợp lệ: thời hạn 1–10080 phút; chu kỳ dọn 5–3600 giây; batch 1–1000 phiên. Cấu hình sai bị từ chối khi khởi động.

Background worker mặc định chạy mỗi 60 giây, mỗi transaction xử lý tối đa 100 phiên, tối đa 10 batch mỗi lượt. Worker mở scope DI riêng và thử lại ở chu kỳ tiếp theo nếu lỗi. Khi nhận token hết hạn trên API bảo vệ, validator cũng yêu cầu đóng phiên nguyên tử; không chờ worker để từ chối token.

SESSION_ENDED của phiên hết hạn ghi đúng expires_at, không ghi thời điểm worker chạy muộn. Logout/thu hồi và tác vụ dọn chạy đua vẫn chỉ tạo một sự kiện; timestamp đầu tiên không bị ghi lại. Tắt worker không làm token hết hạn được chấp nhận; việc ghi lịch sử cho các phiên không còn request sẽ đợi worker được bật trở lại.

Không thay đổi schema/migration. Bootstrap Trưởng phòng đầu tiên vẫn là luồng riêng; không tạo tài khoản hoặc mật khẩu mặc định khi API khởi động.

## Kiểm thử

Chạy `dotnet build CSVC.sln` và `dotnet test CSVC.sln`. Đặt CSVC_POSTGRES_TEST_CONNECTION trỏ đến PostgreSQL UTF-8 có quyền tạo database test để chạy integration tests; mỗi test dùng database riêng và tự dọn, không sửa DB ứng dụng.

Kiểm thử gồm cả ba vai trò, thay thế phiên cũ, truy cập API bằng token được cấp, thông báo lỗi đồng nhất, thất bại đồng thời, ranh giới cửa sổ sai, chặn tạm, khóa quản trị độc lập, logout lặp/đồng thời, expiration đua với request và rollback nếu audit thành công/thất bại không lưu được.
