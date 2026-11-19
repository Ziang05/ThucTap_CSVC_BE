# Kho vật tư dụng cụ và truy vết can thiệp thiết bị

Task 6 triển khai riêng danh mục và tồn vật tư/dụng cụ, không cộng số lượng vào `devices`. Mỗi thiết bị sử dụng tiếp tục giữ số lượng 0 hoặc 1. Giao diện tại `/materials`; mở vật tư từ phiếu sửa chữa/bảo trì và lịch sử can thiệp từ hồ sơ thiết bị. Phân công nhân viên và kiểm kê do thành viên khác phụ trách.

## Quy tắc đã xác nhận

- Nhân viên QTTB ghi nhận nhập, xuất và nhập trả theo quyền vai trò; riêng điều chỉnh tồn cần quản lý duyệt trước khi ghi sổ. Không tự tạo task và không yêu cầu được giao việc trong module này.

- Vật tư thu hồi còn dùng được cộng chung tồn với vật tư mới, giữ tình trạng và nguồn thu hồi trên từng dòng chứng từ. Vật tư hỏng không được nhập theo diện còn dùng được.
- Dụng cụ xuất dùng như vật tư. Không tạo quy trình xuất tạm bắt buộc trả. Có thể nhập trả dụng cụ đã dùng sau khi công việc đã đóng, bằng phiếu thu hồi có liên kết dòng xuất.
- Vật tư phục vụ sửa chữa/bảo trì phải được đối chiếu: **đã dùng + đã nhập trả chưa dùng = đã xuất**, trước khi ghi kết quả cuối của phiếu. Bộ phận tháo được chọn trả kho phải nhập đủ. Với phiếu xuất dùng trực tiếp, chi tiết phiếu cung cấp số đã dùng/đã trả để module của team đối chiếu khi tích hợp.
- Nhập kho QTTB trước rồi xuất riêng. Xuất cho đơn vị khác cần đơn vị nhận xác nhận nhận đủ. Xuất cho công việc tại QTTB do nhân viên QTTB xác nhận nhận vật tư.

## Chứng từ và tồn

Danh mục gồm đơn vị tính (0–3 chữ số thập phân), vật tư hoặc dụng cụ, kho QTTB và vị trí kho. Tạo kho có một vị trí chung; có thể thêm vị trí. Danh mục hỗ trợ sửa, khóa/mở và lưu lịch sử. Không đổi đơn vị/nhóm của vật tư đã có chứng từ; không đổi độ chính xác đơn vị đang được dùng. Thay vị trí bằng phiếu chuyển, không sửa địa điểm của tồn đã phát sinh.

Các phiếu: nhập, xuất dùng, điều chỉnh tăng/giảm, nhập trả chưa dùng, nhập thu hồi còn dùng được và chuyển vị trí. Phiếu giữ nguồn/tham chiếu, lý do, người lập/ghi sổ/thực nhận, thời điểm, liên kết công việc/sửa chữa/yêu cầu và thông tin vật tư tại lúc lập. Có thể sửa phiếu chưa ghi sổ/chưa duyệt hoặc hủy với lý do; không xóa hay sửa chứng từ đã ghi sổ. Sửa sai tồn bằng chứng từ đối ứng hoặc điều chỉnh được duyệt.

Xuất làm giảm tồn khi ghi sổ; ghi nhận vật tư đã dùng không trừ tồn lần hai. Nhập trả chưa dùng liên kết đúng dòng xuất và không vượt phần chưa dùng/chưa trả. Thu hồi bộ phận liên kết bản ghi tháo còn hiệu lực, đúng danh mục, đúng số lượng và tình trạng. Các dòng thu hồi không được đếm hai lần; nhiều dòng tác động cùng số dư vẫn được kiểm tra tổng trong giao dịch.

Ghi sổ chạy trong giao dịch có khóa dùng chung, không cho tồn âm, có kiểm tra phiên bản và chống gửi lặp bằng `requestId`. Khi lưu lịch sử thất bại, tồn và chứng từ cùng rollback. Sổ biến động, lịch sử và bằng chứng sử dụng/can thiệp được bảo vệ chỉ ghi thêm ở PostgreSQL. Dòng phiếu đã ghi sổ không được sửa/xóa/thêm; đầu phiếu chỉ cho chuyển từ chờ nhận sang đã nhận với thông tin người nhận.

Báo cáo theo khoảng thời gian và kho/vị trí có tồn đầu kỳ, nhập, xuất, nhập trả, chuyển vào/ra, điều chỉnh tăng/giảm và tồn cuối kỳ. Sổ biến động có số dư sau từng bút toán, người thực hiện và phiếu nguồn.

## Truy vết thiết bị

Mỗi can thiệp gắn thiết bị và phiếu nghiệp vụ, có loại thay thế/bổ sung/nâng cấp/tháo bỏ/sửa chữa/kiểm tra, nội dung bị tác động, trước–sau, lý do, người thực hiện thực tế và người ghi nhận. Công việc có thể có nhiều can thiệp hoặc không dùng vật tư; không tạo thiết bị con.

Vật tư lắp/dùng ghi nguồn kho QTTB hoặc nguồn nhà cung cấp/đơn vị/nguồn khác. Nguồn kho cần phiếu đã xuất và được nhận đúng công việc, đúng thiết bị; nguồn ngoài giữ mô tả, đơn vị và tham chiếu nguồn. Bộ phận tháo giữ tình trạng, số lượng và cách xử lý: trả kho, trả đơn vị, loại bỏ hoặc giữ cùng thiết bị. Lô/serial lưu khi có.

Hiệu chỉnh can thiệp bằng bản mới thay thế bản cũ trong cùng công việc còn mở, giữ cả hai để đối chiếu. Không viết lại bằng chứng đã gắn phiếu thu hồi hoặc dụng cụ đã nhập trả. Kết quả chuyên môn cuối cùng dùng kết quả và lịch sử của phiếu bảo trì/sửa chữa hiện có; không tạo thêm một can thiệp tổng kết trùng nội dung. Các can thiệp chi tiết vẫn giữ riêng. Vật tư dùng chung cho công việc không gắn thiết bị có bảng ghi nhận sử dụng riêng.

## Yêu cầu PMMS giả lập

Không có kết nối API PMMS thật. Không seed dữ liệu PMMS vào database đang sử dụng.

Dòng vật tư nguồn liên kết với dòng phiếu qua `sourceLineCode`; đơn vị nguồn phải khớp mã/tên đơn vị kho. Một phiếu nhập có thể nhận phần thiếu, phiếu xuất cần đủ toàn bộ các dòng vật tư. Yêu cầu thấy chứng từ vật tư cùng chứng từ thiết bị; ghi sổ được tính là đã phát sinh thực hiện để chặn nhận hủy từ nguồn. Yêu cầu vật tư chỉ hoàn tất sau khi nhận đủ phiếu xuất.

Yêu cầu có cả thiết bị và vật tư xác nhận thực nhận tại phiếu xuất vật tư. Với đơn vị khác, hệ thống đồng thời hoàn tất phiếu cấp phát thiết bị đã sẵn sàng; với QTTB tự dùng, chọn toàn bộ thiết bị để ghi sử dụng cùng lần. Hai phần cùng một giao dịch, lỗi một phần rollback cả hai. API cấp phát/sử dụng riêng chặn bàn giao một phần của yêu cầu hỗn hợp. Dòng dịch vụ độc lập vẫn cần module bằng chứng dịch vụ tương ứng.

## API

Tất cả đường dẫn dưới `/api/v1/materials`; đọc danh mục/tồn/báo cáo và ghi chứng từ dành cho QTTB, quản lý sửa danh mục/duyệt điều chỉnh. Nhân viên đơn vị chỉ đọc phiếu gửi đến đơn vị mình, xác nhận thực nhận và xem can thiệp của thiết bị đang thuộc đơn vị mình.

| Đường dẫn | Chức năng |
| --- | --- |
| `GET/POST /`, `PUT /{code}` | Danh mục vật tư/dụng cụ |
| `GET/POST /units`, `/warehouses`, `/locations`; `PUT /…/{code}` | Danh mục và vị trí |
| `GET /balances`, `/report`, `/movements` | Tồn, nhập–xuất–tồn, sổ biến động |
| `GET/POST /documents`, `GET/PUT /documents/{code}` | Danh sách, lập, đọc, sửa phiếu |
| `POST /documents/{code}/approve`, `/reject`, `/post`, `/receive`, `/cancel` | Duyệt/từ chối điều chỉnh, ghi sổ, nhận đủ, hủy |
| `GET /documents/{code}/history` | Lịch sử chứng từ |
| `GET/POST /changes` | Lịch sử và ghi can thiệp; lọc `deviceCode`/`serviceOrderCode` |
| `GET/POST /uses` | Ghi nhận sử dụng không gắn thiết bị; đọc theo `documentCode` của phiếu xuất |

Các thao tác chứng từ cần `requestId` UUID, hành động/sửa cần `version`. Không dùng lại một `requestId` cho nội dung hoặc hành động khác. Các danh sách có phân trang; bộ chọn giao diện có tìm kiếm và chuyển trang.

## Triển khai

Migration `MaterialInventory` thêm 14 bảng, chỉ số, khóa ngoại, kiểm tra số dư và trigger bảo vệ bằng chứng. Không sửa hoặc xóa thiết bị/chứng từ cũ. Build API và áp dụng migration cùng phiên bản theo [đồng bộ local](local-runtime-sync.md).

Migration `RemoveStaffAssignments` gỡ các bảng phân công đang dùng và khóa ngoại từ chứng từ vật tư. Dữ liệu phân công, lịch sử và các liên kết cũ được lưu tại `retired_assignment_context` trước khi chuyển schema. Các migration đã có giữ nguyên để triển khai được từ database cũ.
