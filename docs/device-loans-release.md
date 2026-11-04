# Triển khai mượn trả hai phòng

Migration: `20261104082423_LoanTwoDepartmentWorkflow`. Triển khai BE/FE đồng bộ: nguồn chọn thiết bị, lender bắt buộc, version mutation, quyền approve và semantics return thay đổi.

## Preflight và migrate

1. Dừng instance API ghi DB; sao lưu bằng `pg_dump -Fc`, kiểm tra archive và lưu ngoài repository.
2. Rà soát các phiếu mượn đang mở qua danh sách, chi tiết và lịch sử; lưu kết quả cùng hồ sơ release. Owner hiện tại là dữ kiện rà soát, không chứng minh lender lịch sử.
3. Kiểm tra phiếu mở, tự mượn/trộn nguồn, operation/service hold. Chuẩn bị bên cho mượn thực tế rà soát phiếu cũ; không tự đổi DepartmentId để làm phiếu hợp lệ.
4. Build/test, build image; chạy `docker compose run --rm api --migrate` rồi khởi động lại API để cập nhật enum mapping. API thường không tự migrate. Down giữ label enum PostgreSQL và từ chối khi có dữ liệu workflow mới; phục hồi bằng backup đã review.
5. Dùng FE mới, kiểm tra readiness/options bằng tài khoản hai phòng. Không tự reset/seed. Migration không đổi tài khoản hoặc mật khẩu.

## Rà soát legacy

Phiếu đóng giữ history. Phiếu mở chưa rà soát trả `LOAN_LEGACY_REVIEW_REQUIRED`, giữ nguyên hold.

Người có quyền đọc toàn cục kiểm tra danh sách, chi tiết, lịch sử phiếu và trạng thái giao nhận thực tế. Sau khi xác định đúng bên cho mượn, chính nhân viên phòng đó đăng nhập và gọi:

```text
POST /api/v1/device-loans/{loanCode}/adopt-legacy
{ "version": <current version>, "evidence": "Căn cứ rà soát và xác nhận tình trạng giao nhận thực tế" }
```

Đây là hành động có audit của bên cho mượn, không phải manager duyệt hộ. Backend không nhận lender/actor từ request: mọi thiết bị phải thuộc phòng actor, khác borrower, hold đúng phiếu nếu đã approve/on-loan và không có service hold xung đột. Evidence bắt buộc; chỉ áp dụng legacy PendingApproval/Approved/OnLoan.

Khi adopt phiếu đã duyệt/giao, decision metadata ghi lender thật rà soát ở thời điểm hiện tại; decision cũ nằm trong beforeData của `LOAN_WORKFLOW_MIGRATED` và history cũ không sửa. Không tạo xác nhận lịch sử giả. Phiếu pending chuyển workflow rồi B quyết định như phiếu mới.

Phiếu tự mượn/nhiều nguồn không được adopt tự động. Giữ nguyên và chuẩn bị biện pháp đối soát/sửa dữ liệu riêng có phê duyệt; endpoint không chuyển ownership hoặc xóa hold. Không gọi lại để ghi đè lần review trước.

## Giới hạn

Loan/Allocation/InterDepartment, khóa thiết bị và sửa cây kiểm tra service hold. Receipt kiểm tra khi nhận hồ sơ có sẵn. Sửa metadata không đổi sở hữu vẫn theo quyền cũ. Writer SQL trực tiếp có thể bỏ qua policy ứng dụng; công cụ bảo trì dữ liệu phải tự tuân thủ transaction/kiểm tra tương ứng.

Recovery được cùng tồn tại service hold, không chiếm operation hold khác. Không xử lý thay export guards/chứng từ nhập lại. Dữ liệu cũ sai lệch như thiết bị còn AwaitingReturn nhưng đã chuyển ra khỏi QTTB cần đối soát vật lý; thu hồi không tự sửa chứng từ xuất/nhập lại.
