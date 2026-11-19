# PMQTTB — Danh sách công việc còn phải làm

Tổng hợp từ [backlog sau rà soát](pmqttb-priority-backlog.md) và [hợp đồng nghiệp vụ](pmqttb-business-contract.md). Danh sách bắt đầu từ các việc còn lại: sửa lệch, bổ sung, tích hợp và nghiệm thu; mục đã hoàn tất được giữ để theo dõi kết quả, không yêu cầu xây lại phần đã có.

Dùng mã **CV-01 → CV-12** cho danh sách mới. Cột “Task cũ” dùng đối chiếu các trao đổi trước. Tất cả checkbox ban đầu để trống; chỉ đánh dấu khi có kết quả kiểm chứng tương ứng. Không suy ra đã triển khai từ việc có code hoặc build thành công.

## Quy tắc áp dụng cho mọi công việc

- Trong phạm vi hiện tại, giữ code đã pull; sửa/bỏ phần từ stash. Công việc cần thay đổi hành vi của code đã pull phải được thống nhất lại về phạm vi trước khi thực hiện.
- Giữ hai file `pmqttb-business-contract.md` và `pmqttb-priority-backlog.md`, dữ liệu và lịch sử hiện hữu. Không xóa/gộp thiết bị cũ để giải quyết khác biệt mô hình.
- Dùng mã vạch, không QR; thiết bị có mã riêng và số lượng `0/1`, vật tư có số lượng nhiều.
- Nhập về QTTB trước rồi cấp phát riêng; yêu cầu chỉ hoàn tất toàn bộ một lần, không nhận hủy sau phát sinh thực hiện.
- PMMS chỉ giả lập; **thông báo cho chủ project trước khi tạo/chạy giả lập**. Không tự seed vào database đang sử dụng hoặc mô tả là kết nối API thật.
- Giữ mượn trả. Không xây lại phân công công việc hoặc kiểm kê do đồng đội phụ trách; không khôi phục module phân công cũ để thay module của team.
- Trên các form cần chọn hồ sơ đã có, ưu tiên danh sách tìm/chọn thay vì bắt nhập mã thủ công.

## Thứ tự ưu tiên

| Mã | Công việc | Ưu tiên | Task cũ | Phụ thuộc / phối hợp |
| --- | --- | --- | --- | --- |
| CV-01 | **Hoàn tất:** kiểm chứng và đồng bộ sau merge | P0 | 0 | [Báo cáo CV-01](cv01-validation.md) |
| CV-02 | Thu hẹp quan hệ thiết bị cha–con | P0 | 2 | Thống nhất phạm vi thay đổi code đã pull |
| CV-03 | Thay in QR bằng in mã vạch | P0 | 8 | Quyết định dùng mã vạch đã chốt; phối hợp phần in đã pull |
| CV-04 | Hoàn thiện yêu cầu PMMS giả lập | P0 | 4 | Chốt dịch vụ độc lập, nghiệm thu sửa chữa, quy tắc chuyên dụng |
| CV-05 | Nghiệm thu nhập → QTTB → cấp phát | P0 | 3 | CV-01; kiểm tra lại nếu CV-02 ảnh hưởng chọn thiết bị |
| CV-06 | Hoàn thiện nghiệm thu kho vật tư/can thiệp | P1 | 6 | CV-01; chốt danh mục sử dụng thực tế |
| CV-07 | Hợp nhất timeline vòng đời thiết bị | P1 | 12 | Dùng lịch sử sẵn có; mở rộng khi nguồn mới hoàn tất |
| CV-08 | Bổ sung thanh lý/xuất vĩnh viễn | P1 | 7 | Chốt quy trình và thẩm quyền |
| CV-09 | Báo cáo thiết bị toàn trường theo kỳ | P1 | 10 | CV-08 cho phần xuất vĩnh viễn |
| CV-10 | Lịch bảo trì/bảo dưỡng và cảnh báo | P2 | 11 | Chốt cách tính lịch và đối tượng nhận cảnh báo |
| CV-11 | Phối hợp tích hợp phân công công việc | Theo team | 5 | Đồng đội cung cấp module và hợp đồng tích hợp |
| CV-12 | Phối hợp nghiệm thu kiểm kê | Theo team | 9 | Module đã pull; phối hợp CV-03 về mã vạch |

CV-11/CV-12 có thể phối hợp song song khi đồng đội sẵn sàng. CV-07 có thể làm trước phần nguồn đã có, không cần chờ thanh lý để bắt đầu.

## P0 — Đồng bộ và sửa lệch nghiệp vụ

### CV-01. Kiểm chứng phiên bản sau merge

**Đã hoàn tất:** backend 336/336 test, UI 63/63 test; build đạt, lint UI 0 lỗi/1 cảnh báo có sẵn. Bảy nhóm tình huống trình duyệt/API đạt; local đã nâng cấp và API chạy tại `http://localhost:8080`. Phạm vi và giới hạn tại [báo cáo CV-01](cv01-validation.md).

- [x] Khôi phục riêng route/menu Mượn trả theo xác nhận, giữ nguyên test đã pull; hai ca lỗi trước đây đã đạt.
- [x] Đối chiếu database/image; áp sáu migration và khởi động API theo xác nhận, live/ready đạt, không còn migration nguồn chờ áp dụng.
- [x] Backup, diễn tập restore/nâng cấp và kiểm tra lưu trữ dữ liệu cũ; gỡ riêng hai guard của stash không còn phù hợp bản pull.
- [x] Kiểm chứng luồng chính bằng API/trình duyệt, gồm PMMS giả lập trên database riêng, vật tư/can thiệp, mượn trả, kiểm kê; sửa lỗi UUID của UI vật tư từ stash.
- [x] Ghi kết quả, giới hạn, bản sao và trạng thái triển khai tại báo cáo CV-01; nghiệm thu chi tiết/khách hàng ở các CV sau vẫn giữ riêng.

**Đạt khi:** biết chính xác phiên bản đang chạy, các luồng đã kiểm chứng và lỗi còn lại; không lẫn lỗi có sẵn với lỗi phần mới.

### CV-02. Thu hẹp quan hệ cha–con

**Hiện tại:** backend/UI vẫn cho gắn/đổi cha; nhập xử lý `ParentDeviceCode`; mượn/điều chuyển vẫn mở rộng cây. Không coi task cũ đã hoàn tất trên phiên bản này.

- [ ] Thống nhất phạm vi sửa với team vì các hành vi trên thuộc code đã pull; xác định cách giữ tham chiếu cũ và xử lý liên kết mới.
- [ ] Kiểm tra dữ liệu cha–con, phiếu đang mở, giữ chỗ và lịch sử; lập phương án bảo toàn dữ liệu trước thay đổi.
- [ ] Đồng bộ API/DB/UI theo phương án được chốt: chọn từng thiết bị/bộ, không bắt khai báo linh kiện thành thiết bị con để chạy nghiệp vụ.
- [ ] Điều chỉnh việc tự mở rộng cây khi chọn/khóa/mượn/điều chuyển và các luồng bị ảnh hưởng; phiếu đã lưu vẫn xử lý đủ dòng hiện hữu.
- [ ] Bổ sung migration nếu cần và kiểm thử dữ liệu cũ, quyền, giữ chỗ, rollback; cập nhật tài liệu theo mô hình cuối cùng.

**Đạt khi:** một thiết bị/bộ có thể xử lý độc lập và truy vết thay đổi bằng can thiệp/vật tư, giữ được phiếu và lịch sử cũ.

### CV-03. In tem mã vạch và hoàn thiện quét tra cứu

**Đã có:** barcode ổn định, API lookup và ô quét/nhập. **Chưa đúng:** phần in hiện dùng `QRCodeSVG`.

- [ ] Thay phần in QR bằng mã vạch trong phạm vi được thống nhất với team; không thêm một màn QR song song. Quyết định dùng mã vạch không cần chốt lại.
- [ ] Chốt mẫu tem, kích thước, nội dung cần in và cách in nhiều thiết bị trong một lần.
- [ ] In lại dùng đúng barcode đã có; không đổi mã thiết bị hoặc tạo định danh mới.
- [ ] Kiểm tra máy quét thực tế mở đúng hồ sơ, mã không tồn tại, quét lặp và quyền đọc theo đơn vị.
- [ ] Phối hợp giao diện quét với module kiểm kê; không tự xây lại nghiệp vụ kiểm kê.

**Đạt khi:** in được tem mã vạch và quét tem mở đúng thiết bị. Có chuỗi barcode trong dữ liệu chưa được tính là hoàn tất in tem.

### CV-04. Hoàn thiện yêu cầu PMMS giả lập

**Đã có:** nhận sự kiện/phiên bản/chống trùng, lịch sử, liên kết thiết bị/vật tư/sửa chữa và kiểm tra hoàn tất toàn bộ.

- [ ] Chốt dữ liệu, người ghi nhận và người xác nhận kết quả **dịch vụ độc lập**; bổ sung bằng chứng/chứng từ tương ứng để yêu cầu có dòng dịch vụ kết thúc thành công được.
- [ ] Chốt đơn vị yêu cầu có phải xác nhận nghiệm thu riêng kết quả sửa chữa không; nếu có, bổ sung bước xác nhận, người/thời điểm và điều kiện đóng yêu cầu.
- [ ] Chốt phân loại chuyên dụng chỉ gợi ý xử lý hay phải ràng buộc cách thực hiện/quyền; triển khai đúng quyết định, không tự mặc định mọi trường hợp bắt buộc bên ngoài.
- [ ] Nghiệm thu yêu cầu thiết bị, vật tư, sửa chữa và yêu cầu hỗn hợp; xác nhận không hoàn tất/bàn giao một phần và không đóng khi còn chứng từ/kết quả chưa đủ.
- [ ] Kiểm tra gửi lại, sự kiện cũ, cập nhật sau khi có chứng từ, hủy trước/sau thực hiện, quyền thao tác và lịch sử.
- [ ] Giữ nhãn giả lập rõ ràng, thông báo trước khi chạy; cập nhật [hướng dẫn PMMS](pmms-simulation.md). Không triển khai API PMMS thật khi chưa có hợp đồng/API nguồn.

**Đạt khi:** mọi loại yêu cầu trong phạm vi được chốt đều có đường kết thúc hợp lệ bằng chứng từ/kết quả thực tế, không phải ghi lý do thất bại để bỏ qua phần thiếu.

### CV-05. Nghiệm thu luồng nhập → QTTB → cấp phát

**Đã có:** code backend/UI và kiểm thử backend. Đây là nghiệm thu sau merge, không xây lại luồng.

- [ ] Kiểm tra phiếu mới nhập vào QTTB/InStorage; đơn vị đề xuất chưa nhận trách nhiệm chỉ vì phiếu được duyệt/hoàn tất nhập.
- [ ] Kiểm tra cấp phát riêng: chỉ khi thực nhận mới đổi đơn vị giữ/InUse; chọn thiết bị từ tồn không tạo thêm lần nhập mới.
- [ ] Kiểm tra QTTB tự dùng: có người/vị trí/lịch sử riêng, không tạo phiếu nguồn và đích cùng phòng.
- [ ] Kiểm tra phiếu phiên bản 1, chọn hồ sơ chưa tiếp nhận, giữ định danh/metadata, quyền, cấu hình QTTB và rollback khi lỗi.
- [ ] Nghiệm thu lại phần chọn thiết bị nếu CV-02 thay đổi mô hình; cập nhật [hướng dẫn nhập/cấp phát](receipt-custody-release.md).

**Đạt khi:** thấy rõ QTTB nhận, giai đoạn giữ và đơn vị thực nhận; hồ sơ/phiếu cũ vẫn xử lý đúng.

## P1 — Kho và vòng đời thiết bị

### CV-06. Nghiệm thu kho vật tư và truy vết can thiệp

**Đã có:** danh mục/đơn vị/kho/vị trí, nhập–xuất–tồn, trả/thu hồi, báo cáo và can thiệp trước–sau.

- [ ] Chốt danh mục dùng thật: vật tư/dụng cụ sửa chữa, thay thế; ranh giới với giấy/mực tiêu hao sử dụng thường ngày đang ngoài phạm vi.
- [ ] Nghiệm thu UI/API nhập, xuất, điều chỉnh, chuyển kho, trả chưa dùng và nhập thu hồi; chỉ điều chỉnh cần quản lý duyệt.
- [ ] Kiểm tra ví dụ tồn 20, xuất 3, còn 17; thiết bị vẫn có số lượng `0/1`. Đối chiếu báo cáo và sổ biến động.
- [ ] Kiểm tra nhiều can thiệp trên nhiều loại thiết bị, cả trường hợp không dùng vật tư; đúng người/thời điểm, trước–sau, nguồn lắp và xử lý phần tháo.
- [ ] Kiểm tra đối soát số dùng/trả trước kết quả sửa chữa, hiệu chỉnh giữ bản gốc, giới hạn thu hồi, tồn âm/đồng thời/gửi lặp/rollback.
- [ ] Xác nhận kết quả tổng kết dùng lịch sử dịch vụ hiện có, không tạo thêm can thiệp tổng kết trùng; cập nhật [hướng dẫn vật tư](material-inventory.md).

**Đạt khi:** đối chiếu được tồn và vật tư sử dụng, lần yêu cầu sau tra được thiết bị đã từng can thiệp gì.

### CV-07. Hợp nhất timeline vòng đời thiết bị

- [ ] Bổ sung báo hỏng, bảo trì/sửa chữa, xuất/nhận lại và can thiệp/vật tư vào timeline đang có DEVICE/RECEIPT/LOAN/TRANSFER.
- [ ] Phối hợp đọc kết quả kiểm kê; bổ sung thanh lý sau CV-08. Dùng lịch sử nguồn, không ghi thêm bản sao sự kiện để dựng timeline.
- [ ] Hiển thị thời gian, loại sự kiện, mã chứng từ, người thực hiện, kết quả, trước–sau và vật tư liên quan; mở được chứng từ nguồn.
- [ ] Giữ phân trang/lọc/phân quyền; tránh nhân đôi sự kiện khi một thao tác ghi nhiều bảng lịch sử.
- [ ] Chốt cách thể hiện thông tin hiện tại đã xác nhận, khác với lịch sử hiệu chỉnh; không suy ra cấu hình thiết bị tùy tiện từ ghi chú gần nhất.

**Đạt khi:** tra một mã thiết bị xem được chuỗi can thiệp và vòng đời trên cùng một luồng lịch sử.

### CV-08. Thanh lý hoặc xuất vĩnh viễn khỏi trường

- [ ] Chốt hình thức chứng từ, trạng thái, thẩm quyền duyệt và bằng chứng/quyết định; phân biệt xuất vĩnh viễn với xuất tạm sửa chữa.
- [ ] Bổ sung quy trình, API/UI ghi thiết bị, người/thời điểm, lý do và kết quả; xử lý thiết bị còn phiếu/giữ chỗ theo quy tắc được chốt.
- [ ] Loại khỏi tồn hiện hữu và chặn cấp phát/mượn/sửa chữa mới sau kết thúc vòng đời, vẫn giữ mã, hồ sơ và lịch sử.
- [ ] Không dùng riêng `quantity=0` để biểu diễn thanh lý; kiểm tra tác động đến timeline và báo cáo.

**Đạt khi:** thiết bị đã ra khỏi trường vĩnh viễn không còn được sử dụng trong nghiệp vụ mới nhưng vẫn tra cứu đầy đủ.

### CV-09. Báo cáo nhập–xuất–tồn thiết bị toàn trường

- [ ] Chốt công thức đầu kỳ/nhập/xuất/cuối kỳ từ chứng từ/lịch sử; cấp phát/thu hồi nội bộ không làm đổi tổng sở hữu.
- [ ] Phân biệt thiết bị chưa tiếp nhận, tài sản đang giữ/sử dụng, xuất tạm dịch vụ và đã xuất vĩnh viễn; lấy dữ liệu thanh lý từ CV-08.
- [ ] Có API/UI lọc theo kỳ, nhóm/danh mục/đơn vị giữ; không chỉ đếm hồ sơ đang có hoặc dùng báo cáo vật tư để thay thế.
- [ ] Đối chiếu chuỗi nhập → cấp phát → thu hồi → sửa bên ngoài → nhận lại → thanh lý và các mốc đầu/cuối kỳ.

**Đạt khi:** số liệu toàn trường đúng theo kỳ, không tăng/giảm vì chuyển nội bộ hoặc xuất tạm đi sửa.

## P2 — Lịch và cảnh báo

### CV-10. Lịch bảo trì/bảo dưỡng

- [ ] Chốt cách tính chu kỳ, mốc bắt đầu, việc bỏ lỡ kỳ, cập nhật kỳ tiếp theo và đối tượng nhận cảnh báo.
- [ ] Khai báo lịch từ hợp đồng khi tạo/nhập thiết bị; tách khỏi hạn bảo hành và phiếu bảo trì thủ công.
- [ ] Có danh sách sắp đến hạn/quá hạn, lọc khoảng thời gian và cảnh báo phù hợp; dùng cơ chế thông báo có sẵn nếu phù hợp.
- [ ] Tạo phiếu bảo trì từ lịch, liên kết lần thực hiện và cập nhật kỳ sau theo kết quả; tránh tạo phiếu/cảnh báo lặp.

**Đạt khi:** nhận biết thiết bị cần bảo trì trước khi có hỏng hóc và truy lại được lịch thực hiện.

## Phối hợp với đồng đội — không xây lại module

### CV-11. Tích hợp phân công công việc

- [ ] Nhận hợp đồng tích hợp từ người phụ trách: định danh yêu cầu/chứng từ, sự kiện, trạng thái, người thực hiện và quyền.
- [ ] Thống nhất thời điểm tạo/liên kết việc, đồng bộ kết quả, hủy và xử lý nhận lặp; không khôi phục module phân công cũ.
- [ ] Kiểm chứng module đồng đội đọc được kết quả/chứng từ và phục vụ báo cáo nhân viên/phòng QTTB; không tự thêm điều kiện được giao việc vào phần này khi chưa thống nhất.

**Đạt khi:** hai phần trao đổi đúng định danh/kết quả, không tạo hai hệ thống phân công song song.

### CV-12. Nghiệm thu kiểm kê và giao diện tích hợp

**Đã có:** module kiểm kê backend/UI từ bản pull. Việc phân người trong đợt kiểm kê không thay module phân công mọi công việc ở CV-11.

- [ ] Phối hợp nghiệm thu kế hoạch/duyệt/thực hiện/hiệu chỉnh/tiến độ/biên bản/thông báo và quyền đọc của từng bên.
- [ ] Đối chiếu yêu cầu hiện diện/tình trạng/vị trí: API kết quả hiện dùng `Exists`/`NewExists`; thống nhất phần còn thiếu với người phụ trách, không tự sửa module đã pull.
- [ ] Phối hợp quét mã vạch theo CV-03 và đưa sự kiện phù hợp vào timeline CV-07.
- [ ] Kiểm tra kết quả kiểm kê không tự ghi đè metadata hoặc thay đổi tồn khi chưa có quy trình đối soát được thống nhất.

**Đạt khi:** module kiểm kê được nghiệm thu theo phạm vi team chốt và tích hợp đúng với hồ sơ, mã vạch, lịch sử thiết bị.

## Các điểm cần chốt trước khi thực hiện phần phụ thuộc

- **Với chủ project:** mẫu tem; bằng chứng dịch vụ độc lập; nghiệm thu sửa chữa; chính sách chuyên dụng; danh mục vật tư; quy trình thanh lý; cách tính lịch bảo trì.
- **Với team:** phạm vi thay đổi cha–con/in tem trong code đã pull; hai lỗi test có sẵn; hợp đồng phân công; phạm vi tình trạng/vị trí/quét kiểm kê.
- Không chốt lại các quyết định đã có: mã vạch, nhập trước/cấp phát riêng, hoàn tất toàn bộ một lần, chặn hủy sau thực hiện và giữ mượn trả.

Hợp đồng nghiệp vụ cốt lõi ở task 1 cũ đã hoàn tất, nên không tạo một task viết lại từ đầu. Cập nhật tài liệu khi từng công việc trên được nghiệm thu, đồng thời ghi rõ phiên bản triển khai và giới hạn còn lại.
