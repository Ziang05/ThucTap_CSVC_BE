# Hợp đồng nghiệp vụ PMQTTB

Phạm vi: task 1 trong [backlog ưu tiên](pmqttb-priority-backlog.md).

## 1. Nguồn và hiệu lực

Tài liệu này thống nhất nghiệp vụ từ `Documents/PMQTTB.docx`, các điều chỉnh của chủ project và các quyết định đã xác nhận. Đây là **nghiệp vụ đích để triển khai**; mô tả đích không tự xác nhận API/migration đã có. Trạng thái triển khai sau merge được đối chiếu tại mục 8 và [backlog ưu tiên](pmqttb-priority-backlog.md).

Thứ tự sử dụng: quyết định mới của chủ project → hợp đồng này → PMQTTB gốc → tài liệu đặc tả cũ. Tài liệu API theo module tiếp tục mô tả hành vi code hiện tại; chỗ khác với hợp đồng này phải được đánh dấu chờ sửa. Các trạng thái của yêu cầu bên dưới không thay thế enum trạng thái phiếu nhập, điều chuyển hoặc sửa chữa hiện có.

### Các quyết định đã xác nhận

| Nội dung | Quyết định |
| --- | --- |
| Nhận hàng và bàn giao | Bắt buộc nhập về QTTB trước, sau đó lập phiếu cấp phát riêng. Không bàn giao thẳng trong lần hoàn tất nhập. |
| Hoàn tất yêu cầu | Hoàn tất toàn bộ yêu cầu một lần; không chia thành nhiều đợt hoàn tất hoặc bàn giao từng phần. |
| Hủy từ PMMS | Không nhận hủy sau khi đã phát sinh thực hiện; QTTB xử lý kết thúc theo nghiệp vụ. |
| Định danh | In và quét mã vạch; thay nội dung QR trong PMQTTB gốc. |
| Thiết bị sử dụng | Quản lý từng bộ/thiết bị cụ thể, mỗi mã có số lượng 0 hoặc 1. |
| Vật tư dụng cụ | Nhóm kho riêng, quản lý theo loại, đơn vị tính và số lượng nhiều. |
| Quan hệ cha–con | Thu hẹp mô hình linh kiện cấu thành; truy vết thay thế, bổ sung, nâng cấp và tháo bỏ qua công việc và vật tư/bộ phận liên quan. |
| Phân công và kiểm kê | Thành viên khác trong team phụ trách; ngoài phạm vi triển khai tại đây. |
| Mượn trả | Giữ như chức năng bổ sung được khách hàng chấp nhận. |
| PMMS | Chưa có API. Chỉ giả lập và phải thông báo cho chủ project trước khi tạo hoặc chạy giả lập. Task 1 chỉ viết hợp đồng. |

## 2. Vai trò và ranh giới hệ thống

| Bên tham gia | Trách nhiệm trong quy trình |
| --- | --- |
| KHTC&ĐT | Liên hệ nhà cung cấp, làm hợp đồng mua hàng/dịch vụ và thanh toán. PMQTTB lưu tham chiếu, tiến độ phối hợp cần thiết. |
| Quản lý QTTB | Theo dõi chứng từ và quyết định nghiệp vụ theo quyền của từng loại phiếu. |
| Nhân viên QTTB | Kiểm tra tồn, nhận hàng, cấp phát/thu hồi, bảo trì/sửa chữa trong khả năng hoặc theo dõi bên ngoài; ghi kết quả thực hiện. |
| Đơn vị yêu cầu/sử dụng | Đề xuất mua sắm/sửa chữa trên PMMS, xác nhận nhận thiết bị/kết quả và chịu trách nhiệm sử dụng, bảo quản sau bàn giao. QTTB cũng có thể là đơn vị sử dụng cuối. |
| Nhà cung cấp/cơ sở dịch vụ | Giao hàng, thực hiện dịch vụ, nhận và trả thiết bị khi xử lý bên ngoài. |

PMMS là nguồn đề xuất mua sắm/sửa chữa. PMQTTB quản lý thiết bị hiện hữu và công việc QTTB sau tiếp nhận, không xây thêm quy trình đấu thầu, hợp đồng hay thanh toán. Không yêu cầu tài khoản/role KHTC&ĐT mới chỉ vì có dữ liệu phối hợp; phân quyền chi tiết của module mới sẽ được thiết kế khi triển khai.

Yêu cầu thông dụng: QTTB kiểm tra tồn trước, dùng tồn phù hợp hoặc phối hợp KHTC&ĐT mua mới/sửa chữa. Yêu cầu chuyên dụng: QTTB theo dõi, ghi nhận và bàn giao; KHTC&ĐT phối hợp đơn vị có chuyên môn và nhà cung cấp. Mức chuyên dụng và cách thực hiện tại chỗ/bên ngoài là hai thuộc tính riêng.

## 3. Hai nhóm kho và đơn vị quản lý

| Nhóm | Đơn vị theo dõi | Quy tắc |
| --- | --- | --- |
| Thiết bị sử dụng | Một bộ/thiết bị theo hợp đồng, một mã và một mã vạch ổn định | `quantity=0` khi hồ sơ chưa tiếp nhận, `quantity=1` sau tiếp nhận. Không gộp nhiều laptop thành một mã số lượng nhiều. |
| Vật tư dụng cụ | Loại vật tư/dụng cụ và đơn vị tính | Có nhiều đơn vị tồn; ghi nhập, xuất, thu hồi/trả kho và điều chỉnh bằng chứng từ. Xuất phục vụ công việc phải liên kết chứng từ/phiếu và thiết bị được xử lý nếu có. |

“Kho toàn trường” bao gồm thiết bị QTTB đang giữ và thiết bị đã cấp phát cho các đơn vị. Cấp phát/thu hồi nội bộ không làm tăng/giảm tổng thiết bị của trường. Xuất tạm đi sửa chữa cần theo dõi vị trí bên ngoài và nhập lại, không được coi là thanh lý hoặc mua mới khi nhận lại. Xuất khỏi trường vĩnh viễn phải có trạng thái vòng đời và chứng từ riêng; không dùng riêng số lượng 0 để suy ra thiết bị chưa từng tiếp nhận.

Mỗi hồ sơ phải phân biệt: đơn vị đề xuất ban đầu; đơn vị hiện chịu trách nhiệm; vị trí thực tế; người phụ trách. QTTB giữ chờ cấp phát/giữ sau thu hồi khác với QTTB sử dụng cho công việc của chính phòng. Trường hợp QTTB tự sử dụng cần ghi nhận đưa vào sử dụng trong phòng, không tạo điều chuyển có nguồn và đích trùng nhau.

Vật tư sửa chữa/thay thế như RAM, bóng đèn thuộc nhóm vật tư dụng cụ. Giấy, mực in tiêu hao thường ngày nằm ngoài phạm vi theo PMQTTB gốc. Danh mục chi tiết và đơn vị tính sẽ được chốt ở task kho vật tư.

## 4. Tiếp nhận, cấp phát và vòng đời thiết bị

### Mua mới

1. Tiếp nhận yêu cầu và kiểm tra tồn nếu phù hợp.
2. Nếu cần mua: theo dõi KHTC&ĐT/nhà cung cấp, tạo phiếu nhập gắn yêu cầu; khai báo từng thiết bị, cấu hình và lịch bảo trì từ hợp đồng nếu có.
3. QTTB xác nhận nhập **toàn bộ** thiết bị của yêu cầu. Lúc này thiết bị do QTTB giữ; đơn vị đề xuất chưa trở thành đơn vị chịu trách nhiệm sử dụng.
4. Lập phiếu cấp phát **riêng**, gắn cùng yêu cầu; đơn vị nhận xác nhận thực nhận đủ thiết bị mới hoàn tất bàn giao và đổi đơn vị chịu trách nhiệm.
5. Đủ chứng từ và kết quả toàn bộ yêu cầu thì đóng yêu cầu một lần. Không tự đóng khi mới duyệt phiếu hoặc nhập kho.

Nhận hàng và cấp phát có thể cùng ngày nhưng vẫn là hai chứng từ và hai sự kiện. Nếu QTTB là đơn vị sử dụng cuối, thay bước chuyển sang phòng khác bằng ghi nhận đưa toàn bộ thiết bị vào sử dụng tại QTTB.

### Dùng tồn, thu hồi và kết thúc vòng đời

Yêu cầu được đáp ứng từ tồn có thể đi thẳng đến phiếu cấp phát, không tạo phiếu nhập mới cho thiết bị đã có. Thu hồi giữ danh tính và lịch sử thiết bị, cập nhật đơn vị giữ về QTTB; cấp phát lại không tạo mã mới. Kiểm kê theo đợt và từng đơn vị ghi hiện diện, vị trí, tình trạng và kết quả đối soát. Thanh lý giữ hồ sơ để tra cứu nhưng loại khỏi tồn hiện hữu và chặn nghiệp vụ sử dụng mới.

Mượn trả tiếp tục dùng quy trình bổ sung hiện tại và không chuyển đơn vị quản lý tài sản. Điều chuyển giữa hai đơn vị vẫn giữ hành vi hiện tại; mức tham gia lâu dài của QTTB cần xác nhận trước khi thay đổi luồng này.

## 5. Truy vết sửa chữa và thay đổi trên thiết bị

Quy tắc này áp dụng chung cho thay thế, bổ sung, nâng cấp, tháo bỏ vật tư/bộ phận và các công việc sửa chữa trên mọi loại thiết bị. Mỗi lần thực hiện phải gắn với đúng thiết bị, yêu cầu nguồn nếu có, phiếu công việc, nhân viên/bên thực hiện, thời điểm, lý do và kết quả. Một công việc có thể có nhiều thay đổi trên cùng thiết bị.

Mỗi thay đổi ghi loại thao tác, đối tượng bị tác động và thông tin trước–sau phù hợp: bộ phận, thông số/cấu hình, tình trạng hoặc kết quả kiểm tra. Khi có vật tư lắp thêm/thay mới, ghi loại, số lượng, đơn vị tính, nguồn cung cấp và liên kết chứng từ xuất dùng nếu lấy từ kho QTTB. Khi tháo ra, ghi bộ phận/vật tư, số lượng và cách xử lý: trả kho, giao lại đơn vị hoặc xử lý theo biên bản. Serial/lô và thông tin bảo hành ghi khi cần; công việc không dùng vật tư vẫn có lịch sử thực hiện.

| Ví dụ minh họa | Thông tin cần truy lại |
| --- | --- |
| Nâng RAM laptop | Dung lượng trước–sau, vật tư đã lắp/tháo, người và thời điểm thực hiện. |
| Thay nguồn máy tính | Nguồn cũ/mới, lý do thay và kết quả kiểm tra; cấu hình tổng thể có thể không thay đổi. |
| Thay bộ phận của quạt hoặc sửa bàn ghế | Bộ phận sửa/thay, vật tư sử dụng và tình trạng trước–sau. |
| Tháo bộ phận hỏng mà chưa lắp mới | Bộ phận đã tháo, nơi tiếp nhận/xử lý và tình trạng thiết bị sau công việc. |

Các ví dụ không phải danh mục cố định. Loại thông tin cần ghi tùy thiết bị và công việc, không bắt mọi thiết bị có trường cấu hình máy tính. Tra cứu mã thiết bị phải thấy được chuỗi thay đổi và thông tin hiện tại đã được xác nhận; sửa sai kết quả cần lịch sử hiệu chỉnh, không ghi đè mất dữ liệu trước đó.

Thiết bị vẫn được định danh theo bộ. Không bắt buộc tạo hồ sơ `Device` con cho từng vật tư/bộ phận để có lịch sử thay đổi. Nếu sau này khách hàng cần theo dõi từng bộ phận qua nhiều thiết bị hoặc bảo hành độc lập, phải bổ sung yêu cầu định danh riêng trước khi mở rộng mô hình.

Task 6 đã bổ sung can thiệp có cấu trúc và vật tư lắp/tháo, xem [kho vật tư và truy vết thiết bị](material-inventory.md). Task 12 tiếp tục hợp nhất các loại sự kiện vào timeline toàn vòng đời.

Nhập/xuất/nhập trả vật tư do nhân viên QTTB ghi nhận trực tiếp; riêng điều chỉnh tồn cần quản lý duyệt. Thu hồi còn dùng được cộng chung tồn với vật tư mới, giữ tình trạng trên chứng từ. Trước khi đóng công việc, số xuất phải được đối chiếu đủ phần đã dùng và phần nhập trả chưa dùng. Dụng cụ quản lý chung như xuất dùng vật tư, không có nghĩa vụ trả bắt buộc; chỉ nhập trả khi thực tế có trả.

## 6. Yêu cầu, chứng từ và ranh giới với module của team

Một yêu cầu nguồn có nhiều dòng thiết bị/dịch vụ, các chứng từ khác loại (nhập, cấp phát, sửa chữa...) cần thiết. Mỗi dòng/chứng từ liên kết về đúng yêu cầu và dòng nguồn; một thiết bị có thể xuất hiện trong nhiều yêu cầu theo thời gian. Liên kết báo hỏng là tùy chọn, vì yêu cầu nâng cấp có thể không xuất phát từ hỏng hóc.

Phân công nhân viên, hạn/tiến độ và báo cáo công việc thuộc module do thành viên khác trong team phụ trách. Kiểm kê cũng nằm ngoài phần việc này. Không tự sinh task hoặc bắt buộc phải được giao việc để xử lý chứng từ tại đây; quyền thực hiện và quyền duyệt tiếp tục theo vai trò của từng phiếu.

Vật tư và lịch sử can thiệp liên kết trực tiếp với phiếu nhập/xuất, phiếu sửa chữa/bảo trì và thiết bị. Nhân viên QTTB ghi nhận người thực hiện thực tế, thời điểm và kết quả. Một chứng từ hoàn tất chưa đồng nghĩa yêu cầu đã hoàn tất; cần đủ kết quả và xác nhận của toàn bộ các dòng.

### Trạng thái yêu cầu đề xuất để triển khai

| Trạng thái | Điều kiện vào và bước tiếp theo |
| --- | --- |
| Đã tiếp nhận | Nguồn hợp lệ; chuyển Đang xử lý hoặc Hủy nếu chưa phát sinh thực hiện. |
| Đang xử lý | Kiểm tra tồn, chuẩn bị chứng từ hoặc thực hiện công việc; có thể chuyển Chờ hàng/Chờ bên ngoài, Chờ bàn giao, Đã hoàn tất hoặc Hủy khi đủ điều kiện. |
| Chờ hàng | Cần nhận hàng/vật tư; trở về Đang xử lý khi có hàng hoặc chuyển Chờ bên ngoài nếu đổi tình huống. |
| Chờ bên ngoài | Chờ KHTC&ĐT, đơn vị chuyên môn hoặc nhà cung cấp; trở về Đang xử lý/Chờ hàng khi có kết quả. |
| Chờ bàn giao | Đã sẵn sàng toàn bộ thiết bị/kết quả cần bàn giao; trở về Đang xử lý nếu chưa được chấp nhận, hoặc chuyển Đã hoàn tất khi thực nhận đủ. |
| Đã hoàn tất | Toàn bộ dòng đã có kết quả cuối và xác nhận; trạng thái cuối, ghi kết quả thành công/thất bại cùng lý do. Không hoàn tất một phần. |
| Hủy | Chỉ trước khi phát sinh thực hiện; ghi lý do, nguồn hủy và người/thời gian xử lý. Trạng thái cuối. |

Hủy từ trạng thái chưa kết thúc phải kiểm tra dữ liệu thực tế, không chỉ tên trạng thái. Quy tắc bảo thủ đề xuất: bắt đầu thực hiện chuyên môn, nhận hàng, xuất vật tư, bàn giao hoặc đưa thiết bị ra ngoài là đã phát sinh thực hiện. Việc kiểm tra tồn/chuẩn bị chưa tự động chặn hủy. Giữ chỗ/phiếu đang duyệt cần được hủy hợp lệ và giải phóng trước khi kết thúc yêu cầu; không bỏ qua ràng buộc hiện tại của chứng từ đã duyệt.

Đề nghị hủy không hợp lệ được ghi vào lịch sử tiếp nhận, kèm lý do không áp dụng; yêu cầu vẫn tiếp tục xử lý. Kết quả ghi nhận từ PMMS không được ghi đè trạng thái thực hiện nội bộ. Tên trạng thái/kỹ thuật lưu trữ cuối cùng được quyết định khi triển khai module.

## 7. Hợp đồng dữ liệu PMMS giả lập

Task 1 thống nhất thiết kế dữ liệu tối thiểu; task 4 đã bổ sung [API và giao diện giả lập](pmms-simulation.md) sau khi thông báo chủ project. Đây chưa phải định dạng API thật của PMMS. Dữ liệu và màn hình luôn thể hiện nguồn mô phỏng; không tự seed dữ liệu giả lập vào môi trường đang chạy.

| Trường/nhóm dữ liệu | Quy tắc đề xuất |
| --- | --- |
| Nguồn + mã yêu cầu | Nguồn mô phỏng riêng, ví dụ `PMMS_SIMULATED`; mã yêu cầu duy nhất trong nguồn và bất biến. PMMS thật sau này có định danh nguồn riêng. |
| Mã sự kiện + phiên bản | Định danh lần gửi và số phiên bản tăng dần để xử lý gửi lại/sai thứ tự; không dùng thời gian gửi làm khóa duy nhất. |
| Thao tác | Tạo, cập nhật hoặc đề nghị hủy; hủy tham chiếu yêu cầu đã có. |
| Loại yêu cầu | Mua sắm hoặc sửa chữa/nâng cấp; nội dung hàng hóa/vật tư/dịch vụ ghi ở từng dòng. |
| Đơn vị đề xuất | Mã đơn vị bắt buộc, thông tin người liên hệ nếu có; phải ánh xạ được phòng ban nội bộ. Không ánh xạ được thì giữ lỗi chờ đối soát, chưa thực hiện. |
| Dòng yêu cầu | Mã dòng nguồn ổn định, nhóm hàng/dịch vụ, tên/mô tả, số lượng và đơn vị tính; mức thông dụng/chuyên dụng hoặc chưa xác định để QTTB phân loại. |
| Thiết bị đích | Bắt buộc với sửa chữa/nâng cấp: mã thiết bị đã có hoặc tham chiếu cần đối soát. Mua mới có thể chưa có mã thiết bị, sẽ liên kết khi khai báo/nhập. |
| Thời gian và trạng thái nguồn | Ngày tạo, cập nhật nguồn, trạng thái nguồn; lưu riêng với thời điểm tiếp nhận và trạng thái nội bộ PMQTTB. |
| Tham chiếu bổ sung | Mã hợp đồng, nhà cung cấp, mô tả kỹ thuật/lịch bảo trì khi có; không bắt buộc đã có hợp đồng lúc mới đề xuất. |

### Gửi lại, cập nhật và hủy

- Cùng nguồn/mã yêu cầu không tạo thêm yêu cầu; cùng sự kiện/phiên bản/nội dung trả lại kết quả tiếp nhận trước đó. Cùng khóa khác nội dung là xung đột để đối soát.
- Sự kiện cũ đến muộn không ghi đè phiên bản mới. Bản cập nhật mới lưu lịch sử nguồn và phần thay đổi; trước thực hiện có thể áp dụng dữ liệu hợp lệ và đối soát lại phiếu chưa thực hiện.
- Sau khi đã phát sinh thực hiện, thay đổi số lượng, thiết bị, đơn vị nhận hoặc phạm vi không tự sửa chứng từ. Ghi thay đổi chờ đối soát; phần phát sinh thêm phải thành yêu cầu mới theo quy tắc hoàn tất một lần.
- Hủy trước thực hiện chỉ được chấp nhận khi các phiếu/giữ chỗ đã giải quyết hợp lệ. Hủy sau thực hiện bị từ chối áp dụng, giữ lịch sử và lý do; QTTB tiếp tục kết thúc nghiệp vụ. Hủy không xóa hồ sơ, chứng từ hay kết quả.
- Yêu cầu Đã hoàn tất/Hủy không mở lại tự động từ sự kiện nguồn. Tiếp nhận dữ liệu không được ghi đè người thực hiện hoặc lịch sử nội bộ.

## 8. Khoảng cách với code hiện tại và đầu việc thực hiện

| Phần | Hiện tại | Công việc tiếp theo |
| --- | --- | --- |
| Mã vạch | Có mã ổn định, lookup và ô quét/nhập; UI hiện có in tem nhưng dùng QR, chưa khớp quyết định mã vạch | Task 8 nâng lên P0; thống nhất sửa phần UI đã pull |
| Thiết bị theo bộ | Chưa đạt trên bản sau pull: vẫn có ghi quan hệ cha–con và mở rộng cây; migration/tài liệu thu hẹp cũ không còn trong source | Task 2 chờ thống nhất với team; giữ code đã pull trong lúc ghép stash, bảo toàn dữ liệu/lịch sử |
| Nhập rồi cấp phát | Có backend/UI tách phòng đề xuất và QTTB tiếp nhận, giữ nghĩa phiếu cũ; luồng chính đã kiểm chứng và local đã đồng bộ ở CV-01 | Nghiệm thu chi tiết ở CV-05; [hướng dẫn](receipt-custody-release.md), [báo cáo CV-01](cv01-validation.md) |
| PMMS và yêu cầu nội bộ | Có giả lập, phiên bản/sự kiện/lịch sử, liên kết nhập/cấp phát/sửa chữa/vật tư; chưa có bằng chứng hoàn tất dịch vụ độc lập | Task 4 còn phần dịch vụ; làm rõ nghiệm thu sửa chữa/quy tắc chuyên dụng; chưa kết nối PMMS thật |
| Phân công nhân viên và kiểm kê | Đồng đội phụ trách. Kiểm kê đã có backend/UI từ bản pull; phân công mọi công việc là module riêng | Không xây trùng; phối hợp nghiệm thu tình trạng/vị trí/quét kiểm kê và giao diện giữa module |
| Vật tư và thay đổi trên thiết bị | Có backend/UI, tồn, chứng từ, đối chiếu sử dụng, lắp/tháo và can thiệp trước–sau; kết quả tổng kết dùng lịch sử dịch vụ hiện có | Task 6 cần nghiệm thu sau merge; task 12 hợp nhất timeline, không tạo sự kiện trùng |
| Toàn bộ một lần | Yêu cầu mới kiểm tra đủ mọi dòng, một phiếu cấp phát đủ yêu cầu và thực nhận trước khi đóng; sửa chữa cần kết quả đủ và giải phóng giữ chỗ | API sửa chữa/xuất cũ vẫn ghi kết quả từng item; không suy ra hoàn tất yêu cầu từ một item |
| Thanh lý/lịch bảo trì | Chưa có các module tương ứng | Task 7, 11 |
| Timeline và báo cáo | Timeline có DEVICE/RECEIPT/LOAN/TRANSFER; can thiệp xem riêng. Có báo cáo vật tư, chưa có nhập–xuất–tồn thiết bị toàn trường theo kỳ | Task 12 nâng lên P1; task 10 phụ thuộc thanh lý ở task 7 |

## 9. Tình huống nghiệm thu cho các task triển khai

1. CNTT yêu cầu hai laptop: nhập đủ về QTTB, vẫn chưa thuộc CNTT; phiếu cấp phát riêng bàn giao đủ hai máy mới đổi trách nhiệm và hoàn tất yêu cầu. Không có hoàn tất một máy trước.
2. Có sẵn thiết bị trong trường: cấp phát từ tồn, không nhập mới hoặc tăng tổng tồn toàn trường.
3. QTTB cần laptop cho chính phòng: ghi nhận dùng nội bộ, phân biệt với hàng giữ chờ cấp phát, không tạo phiếu nguồn/đích trùng phòng.
4. Thực hiện các công việc thay thế, bổ sung, nâng cấp và tháo bỏ trên nhiều loại thiết bị: truy được ai làm, lúc nào, thay đổi gì, thông tin trước–sau, vật tư lắp/tháo và cách xử lý nếu có. Bao gồm trường hợp thay cùng thông số, công việc không dùng vật tư và một lần có nhiều thay đổi; lần yêu cầu tiếp theo xem được lịch sử và thông tin hiện tại đã xác nhận.
5. PMMS giả lập gửi lại: không nhân bản yêu cầu/chứng từ; cập nhật cũ không ghi đè mới; hủy trước thực hiện có lịch sử, hủy sau nhận hàng hoặc sửa chữa không được áp dụng.
6. Thu hồi, bảo trì, mượn trả và thanh lý giữ mã thiết bị, lịch sử và phạm vi quyền; xuất tạm sửa chữa không làm mất tài sản khỏi tổng sở hữu toàn trường.

Các tình huống trên là tiêu chí cho bước triển khai sau, chưa phải kết quả chạy kiểm thử trong task 1.
