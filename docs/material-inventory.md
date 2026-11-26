# Kho vật tư dụng cụ và truy vết can thiệp thiết bị

Task 6 triển khai riêng danh mục và tồn vật tư/dụng cụ, không cộng số lượng vào `devices`. Mỗi thiết bị sử dụng tiếp tục giữ số lượng 0 hoặc 1. Giao diện tại `/materials`; mở vật tư từ phiếu sửa chữa/bảo trì và lịch sử can thiệp từ hồ sơ thiết bị. Vật tư nhập về thuộc phòng QTTB, không chia kệ/ngăn/vị trí trên giao diện. Phân công nhân viên và kiểm kê do thành viên khác phụ trách.

## Quy tắc đã xác nhận

- Nhân viên QTTB ghi nhận nhập và xuất. Bên nhận của phiếu xuất ghi số đã dùng và lập phiếu trả chưa dùng hoặc trả dụng cụ đã dùng; nhân viên/quản lý QTTB duyệt phiếu trả và đồng thời ghi sổ nhập lại kho. Điều chỉnh tồn vẫn cần quản lý duyệt. Chứng từ gắn yêu cầu sửa chữa tiếp tục kiểm tra nhân viên được giao khi xuất; thao tác sử dụng/trả thuộc đơn vị nhận, duyệt trả thuộc QTTB.

- Vật tư thu hồi còn dùng được cộng chung tồn với vật tư mới, giữ tình trạng và nguồn thu hồi trên từng dòng chứng từ. Vật tư hỏng không được nhập theo diện còn dùng được.
- Dụng cụ xuất dùng như vật tư. Không tạo quy trình xuất tạm bắt buộc trả. Có thể nhập trả dụng cụ đã dùng sau khi công việc đã đóng, bằng phiếu thu hồi có liên kết dòng xuất.
- Vật tư phục vụ sửa chữa/bảo trì phải được đối chiếu: **đã dùng + đã nhập trả chưa dùng = đã xuất**, trước khi ghi kết quả cuối của phiếu. Bộ phận tháo được chọn trả kho phải nhập đủ. Với phiếu xuất dùng trực tiếp, chi tiết phiếu cung cấp số đã dùng/đã trả để module của team đối chiếu khi tích hợp.
- Nhập kho QTTB trước rồi xuất riêng. Phiếu chỉ xuất vật tư hoàn tất khi ghi sổ, không yêu cầu xác nhận nhận thêm. Yêu cầu hỗn hợp thiết bị và vật tư vẫn giữ bước bàn giao phối hợp hiện có.

## Chứng từ và tồn

Danh mục trên giao diện gồm **Loại vật tư / dụng cụ** và **Đơn vị tính** (0–3 chữ số thập phân). Mỗi loại có mã riêng, tên, phân loại vật tư/dụng cụ và đơn vị tính; phiếu nhập/xuất lưu liên kết tới đúng mã danh mục và thông tin tại lúc lập. Nhân viên và quản lý QTTB được thêm loại/đơn vị mới; chỉ quản lý được sửa mục đã có. Không quản lý trạng thái ở cả hai danh mục trên giao diện. Không đổi đơn vị/nhóm của vật tư đã có chứng từ; không đổi độ chính xác đơn vị đang được dùng. Metadata trạng thái và API cũ được giữ để tương thích dữ liệu hiện hữu.

Tab Danh mục có nút **Thêm loại vật tư**. Khi lập phiếu nhập, chọn loại có sẵn hoặc dùng **Thêm loại vật tư mới** ngay tại dòng nhập. Có thể tạo đơn vị tính ngay trong form thêm loại. Sau khi lưu, loại/đơn vị vừa tạo được tự chọn, giữ dữ liệu phiếu đang điền. Xuất dùng chọn lại từ cùng danh mục; không tạo mã danh mục mới cho từng lần nhập/xuất. Tạo danh mục chưa làm tăng tồn; chỉ ghi sổ nhập mới cộng tồn.

Các bộ chọn vật tư, đơn vị tính và trường liên quan chỉ hiển thị dropdown, không kèm textbox tìm kiếm hoặc nút chuyển trang. Hệ thống tải đủ các trang dữ liệu cho dropdown. Đơn vị tính hiển thị tên như “cái”, “bộ”, “mét”; mã vẫn được gửi trong payload để liên kết chính xác. Không xóa mã hoặc dùng tên thay khóa dữ liệu.

Người dùng không phải tạo kho hoặc chọn vị trí. Phiếu mới bỏ `locationCode` để backend tự ghi nhận vào tồn phòng QTTB theo cấu hình; dữ liệu tài khoản tồn nội bộ được tạo cùng giao dịch lập phiếu đầu tiên. Mã kho/vị trí và API cũ được giữ cho tương thích chứng từ/lịch sử, không xóa hoặc viết lại dữ liệu đã ghi sổ. Chứng từ mới không có lựa chọn chuyển kệ/ngăn trên giao diện.

Các phiếu hiện dùng: nhập, xuất dùng, điều chỉnh tăng/giảm, nhập trả chưa dùng và nhập thu hồi còn dùng được. Phiếu chuyển vị trí cũ vẫn được đọc/xử lý để bảo toàn dữ liệu. Phiếu giữ nguồn/tham chiếu, lý do, người lập/ghi sổ/thực nhận, thời điểm, liên kết công việc/sửa chữa/yêu cầu và thông tin vật tư tại lúc lập. Có thể sửa phiếu chưa ghi sổ/chưa duyệt hoặc hủy với lý do; không xóa hay sửa chứng từ đã ghi sổ. Sửa sai tồn bằng chứng từ đối ứng hoặc điều chỉnh được duyệt.

Xuất làm giảm tồn khi ghi sổ; ghi nhận vật tư đã dùng không trừ tồn lần hai. Nhập trả chưa dùng liên kết đúng dòng xuất và không vượt phần chưa dùng/chưa trả. Thu hồi bộ phận liên kết bản ghi tháo còn hiệu lực, đúng danh mục, đúng số lượng và tình trạng. Các dòng thu hồi không được đếm hai lần; nhiều dòng tác động cùng số dư vẫn được kiểm tra tổng trong giao dịch.

Phiếu trả do bên nhận lập ở trạng thái `PendingApproval`, giữ nguyên kho, danh mục, đơn vị tính và dòng xuất nguồn. Phiếu chờ duyệt chưa tăng tồn nhưng giữ phần số lượng trả để không thể ghi dùng hoặc gửi trả trùng. QTTB duyệt sẽ kiểm tra lại số lượng và cộng tồn trong cùng giao dịch; từ chối phải ghi lý do và giải phóng phần giữ này. Các quyền `canRecordConsumption`, `canSubmitReturn`, `canApproveReturn` và số trả chờ duyệt được trả trong chi tiết phiếu. Bên xuất không được ghi sử dụng/trả thay một đơn vị nhận khác.

Ghi sổ chạy trong giao dịch có khóa dùng chung, không cho tồn âm, có kiểm tra phiên bản và chống gửi lặp bằng `requestId`. Khi lưu lịch sử thất bại, tồn và chứng từ cùng rollback. Sổ biến động, lịch sử và bằng chứng sử dụng/can thiệp được bảo vệ chỉ ghi thêm ở PostgreSQL. Dòng phiếu đã ghi sổ không được sửa/xóa/thêm; đầu phiếu chỉ cho chuyển từ chờ nhận sang đã nhận với thông tin người nhận.

Báo cáo trên giao diện theo khoảng thời gian có tồn đầu kỳ, nhập, xuất, nhập trả, điều chỉnh tăng/giảm và tồn cuối kỳ tại QTTB. Sổ biến động có số dư sau từng bút toán, người thực hiện và phiếu nguồn. Chi tiết kho/vị trí cũ vẫn giữ trong API và lịch sử.

## Truy vết thiết bị

Mỗi can thiệp gắn thiết bị và phiếu nghiệp vụ, có loại thay thế/bổ sung/nâng cấp/tháo bỏ/sửa chữa/kiểm tra, nội dung bị tác động, trước–sau, lý do, người thực hiện thực tế và người ghi nhận. Công việc có thể có nhiều can thiệp hoặc không dùng vật tư; không tạo thiết bị con.

Form vật tư lắp/dùng luôn dùng nguồn kho QTTB. Người ghi chọn vật tư từ danh mục, số lượng, tình trạng, lô/serial nếu có và điền đơn vị nhận, người nhận, lý do, ngày/chứng từ tham chiếu, ghi chú của phiếu xuất mới. `POST /changes` nhận nhóm `issue` và lập/ghi sổ phiếu xuất cùng can thiệp trong một giao dịch. Không chọn phiếu xuất cũ, không tự ghi số đã dùng. Lịch sử giữ `issueDocumentCode` để mở phiếu xuất; bên nhận ghi sử dụng và trả phần dư sau đó. Thiết bị mở từ hồ sơ được cố định trong form.

Bằng chứng vật tư theo luồng cũ vẫn được giữ để tra cứu/hiệu chỉnh; hiệu chỉnh mô tả can thiệp giữ liên kết phiếu xuất đã ghi sổ, không tự tạo thêm phiếu hoặc thay đổi tồn. Bộ phận tháo giữ tình trạng, số lượng và cách xử lý: trả kho, trả đơn vị, loại bỏ hoặc giữ cùng thiết bị. Lô/serial lưu khi có.

Hiệu chỉnh can thiệp bằng bản mới thay thế bản cũ trong cùng công việc còn mở, giữ cả hai để đối chiếu. Không viết lại bằng chứng đã gắn phiếu thu hồi hoặc dụng cụ đã nhập trả. Kết quả chuyên môn cuối cùng dùng kết quả và lịch sử của phiếu bảo trì/sửa chữa hiện có; không tạo thêm một can thiệp tổng kết trùng nội dung. Các can thiệp chi tiết vẫn giữ riêng. Vật tư dùng chung cho công việc không gắn thiết bị có bảng ghi nhận sử dụng riêng.

## Yêu cầu PMMS giả lập

Không có kết nối API PMMS thật. Không seed dữ liệu PMMS vào database đang sử dụng.

Dòng vật tư nguồn liên kết với dòng phiếu qua `sourceLineCode`; đơn vị nguồn phải khớp mã/tên đơn vị kho. Một phiếu nhập có thể nhận phần thiếu, phiếu xuất cần đủ toàn bộ các dòng vật tư. Yêu cầu thấy chứng từ vật tư cùng chứng từ thiết bị; ghi sổ được tính là đã phát sinh thực hiện để chặn nhận hủy từ nguồn. Yêu cầu vật tư chỉ hoàn tất sau khi nhận đủ phiếu xuất.

Yêu cầu có cả thiết bị và vật tư xác nhận thực nhận tại phiếu xuất vật tư. Với đơn vị khác, hệ thống đồng thời hoàn tất phiếu cấp phát thiết bị đã sẵn sàng; với QTTB tự dùng, chọn toàn bộ thiết bị để ghi sử dụng cùng lần. Hai phần cùng một giao dịch, lỗi một phần rollback cả hai. API cấp phát/sử dụng riêng chặn bàn giao một phần của yêu cầu hỗn hợp. Dịch vụ độc lập dùng bằng chứng và nghiệm thu tại [module yêu cầu](pmms-simulation.md).

## API

Tất cả đường dẫn dưới `/api/v1/materials`; đọc danh mục/tồn/báo cáo và ghi chứng từ dành cho QTTB. Nhân viên QTTB được thêm loại vật tư và đơn vị tính; chỉ quản lý sửa danh mục và duyệt điều chỉnh. API cũ về trạng thái không bị xóa, nhưng không có thao tác quản lý trạng thái trên giao diện hai danh mục. Nhân viên đơn vị chỉ đọc phiếu gửi đến đơn vị mình, xác nhận thực nhận và xem can thiệp của thiết bị đang thuộc đơn vị mình.

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

## Kiểm chứng luồng danh mục và tồn tại QTTB

- 28 test backend liên quan đạt, gồm tạo loại/đơn vị bằng tài khoản nhân viên QTTB, quyền sửa của quản lý, nhập/xuất/trả/điều chỉnh không truyền vị trí, gửi lặp, đối chiếu tồn và rollback tài khoản tồn tự tạo khi audit lỗi. Kết quả tại `tests/CSVC.API.Tests/TestResults/material-catalog-final.trx`.
- Frontend build/lint đạt; 71 kiểm thử hợp đồng đạt. Có kiểm tra cả hai danh mục không có trạng thái, tạo nhanh giữ phiếu đang điền, dropdown chỉ hiện tên và tải đủ mục qua nhiều trang API.
- Bốn kịch bản trình duyệt dùng API/PostgreSQL thật đạt: tạo loại/đơn vị trong Danh mục; nhập loại có sẵn về QTTB; tạo nhanh loại mới khi nhập; xuất bằng cùng mã danh mục và đối chiếu tồn. Chạy trên database kiểm thử riêng; kết quả tại `artifacts/material-catalog-browser/result.json`.
- API local tại `http://localhost:8080` đã cập nhật, live/ready đạt. Không thay schema hoặc seed dữ liệu mẫu. Loại/đơn vị hiện có, tồn, chứng từ và hồ sơ thiết bị giữ nguyên; đã backup tại `%LOCALAPPDATA%/CSVC/backups/material-catalog-7d6e3f70/database.dump`. Biên bản máy tại `artifacts/material-catalog-deployment.json`.

Đây là kiểm chứng riêng các thay đổi danh mục/nhập xuất, không thay thế nghiệm thu toàn bộ CV-06.
