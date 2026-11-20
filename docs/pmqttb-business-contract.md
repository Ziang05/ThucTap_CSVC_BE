# Hợp đồng nghiệp vụ PMQTTB

**Cập nhật triển khai:** đã có phân công trực tiếp trên yêu cầu PMMS, chặn thao tác theo người được giao và bắt đầu sửa chữa từ nguồn; API local đã cập nhật. Xem [hướng dẫn, phạm vi và kiểm chứng hiện tại](source-request-assignment.md). CV-11 vẫn còn phần module chung cho nghiệp vụ nội bộ; các ghi chú rà soát bên dưới mô tả khoảng thiếu trước bản cập nhật này.

Phạm vi: task 1 trong [backlog ưu tiên](pmqttb-priority-backlog.md).

## 1. Nguồn và hiệu lực

Tài liệu này thống nhất nghiệp vụ từ `Documents/PMQTTB.docx`, các điều chỉnh của chủ project và các quyết định đã xác nhận. Đây là **nghiệp vụ đích để triển khai**; mô tả đích không tự xác nhận API/migration đã có. Trạng thái triển khai sau merge được đối chiếu tại mục 8 và [backlog ưu tiên](pmqttb-priority-backlog.md).

Thứ tự sử dụng: quyết định mới của chủ project → PMQTTB gốc → hợp đồng diễn giải này → tài liệu đặc tả cũ. Khi hợp đồng diễn giải khác tài liệu gốc mà không có điều chỉnh của chủ project, phải sửa hợp đồng. Tài liệu API theo module mô tả hành vi code hiện tại, không tự xác nhận hành vi đó đúng nghiệp vụ.

**Đính chính ranh giới nguồn và task:** đã đọc lại toàn bộ `Documents/PMQTTB.docx`. Yêu cầu mua sắm và yêu cầu sửa chữa đều được đơn vị lập trên PMMS rồi kế thừa sang PMQTTB. PMQTTB không lập lại hai yêu cầu này. Phân task cho nhân viên QTTB là nghiệp vụ cốt lõi của phần mềm; việc đồng đội phụ trách module chỉ phân chia trách nhiệm triển khai. Luồng hiện tại còn thiếu tích hợp task và còn màn tạo phiếu sửa chữa cục bộ cần chỉnh. Kết quả test trước đây phản ánh hành vi code đã có, không thay thế đối chiếu nghiệp vụ nguồn.

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
| Phân công và kiểm kê | Là nghiệp vụ của PMQTTB. Đồng đội phụ trách module; phải tích hợp với yêu cầu/chứng từ, không dựng hệ thống phân công thứ hai hoặc coi phân công là tùy chọn. |
| Mượn trả | Giữ như chức năng bổ sung được khách hàng chấp nhận. |
| Nguồn yêu cầu | PMMS lập yêu cầu mua sắm/sửa chữa. PMQTTB kế thừa, theo dõi, phân công và ghi nhận thực hiện; không lập lại yêu cầu ở PMQTTB. |
| PMMS | Chưa có API. Chỉ giả lập phía nguồn PMMS và phải thông báo trước khi chạy; công cụ giả lập không phải bước tác nghiệp tạo yêu cầu của QTTB. |

## 2. Vai trò và ranh giới hệ thống

| Bên tham gia | Trách nhiệm trong quy trình |
| --- | --- |
| KHTC&ĐT | Liên hệ nhà cung cấp, làm hợp đồng mua hàng/dịch vụ và thanh toán. PMQTTB lưu tham chiếu, tiến độ phối hợp cần thiết. |
| Quản lý QTTB | Theo dõi yêu cầu kế thừa từ PMMS, phân công nhân viên phụ trách, theo dõi tiến độ và duyệt nghiệp vụ theo thẩm quyền. |
| Nhân viên QTTB | Nhận task được giao; kiểm tra tồn, nhận hàng, cấp phát/thu hồi, sửa chữa trong khả năng hoặc theo dõi phối hợp; ghi kết quả và xác nhận hoàn tất khi đủ bằng chứng. |
| Đơn vị yêu cầu/sử dụng | Đề xuất mua sắm/sửa chữa trên PMMS, xác nhận nhận thiết bị/kết quả và chịu trách nhiệm sử dụng, bảo quản sau bàn giao. QTTB cũng có thể là đơn vị sử dụng cuối. |
| Nhà cung cấp/cơ sở dịch vụ | Giao hàng, thực hiện dịch vụ, nhận và trả thiết bị khi xử lý bên ngoài. |

PMMS là nguồn duy nhất khởi tạo đề xuất mua sắm/sửa chữa của đơn vị. PMQTTB kế thừa hồ sơ nguồn để theo dõi và phân công; không bắt đơn vị hoặc QTTB tạo một yêu cầu/phiếu sửa chữa thứ hai mới được xử lý. Phiếu nhập, cấp phát, xuất vật tư, bàn giao/nhận lại và nhật ký thực hiện là chứng từ phát sinh để giải quyết yêu cầu đã có. Nếu giữ cấu trúc `ServiceOrder` bên trong, cấu trúc đó phải phục vụ ghi nhận thực hiện của yêu cầu/task, không là một yêu cầu sửa chữa mới hoặc vòng duyệt yêu cầu trùng PMMS.

KHTC&ĐT phụ trách nhà cung cấp, hợp đồng và thanh toán. PMQTTB chỉ ghi tham chiếu và tình trạng phối hợp cần theo dõi; không xây thêm quy trình đấu thầu, hợp đồng hoặc thanh toán.

Yêu cầu thông dụng: QTTB kiểm tra tồn trước, dùng tồn phù hợp hoặc phối hợp KHTC&ĐT mua mới/sửa chữa. Yêu cầu chuyên dụng: QTTB theo dõi, ghi nhận và bàn giao; KHTC&ĐT phối hợp đơn vị có chuyên môn và nhà cung cấp. Mức chuyên dụng và cách thực hiện tại chỗ/bên ngoài là hai thuộc tính riêng.

### Các tình huống trong tài liệu nguồn

| Tình huống | Luồng và vai trò QTTB |
| --- | --- |
| Mua món thông dụng, đủ tồn | Kế thừa yêu cầu PMMS → giao task → kiểm tra tồn → cấp phát từ tồn cho đơn vị; không mua/nhập thêm để hợp thức hóa. |
| Mua món thông dụng, thiếu tồn | Nhân viên được giao kiểm tra tồn, phối hợp đề xuất KHTC&ĐT mua phần thiếu; khi hàng về ghi nhận nhập QTTB, sau đó cấp phát riêng. Không tạo lại đề xuất mua sắm nguồn trên PMQTTB. |
| Mua thiết bị chuyên dụng | KHTC&ĐT và đơn vị đề xuất làm việc với nhà cung cấp. Nhân viên QTTB được giao theo dõi, ghi nhận tiếp nhận và bàn giao. |
| Sửa chữa thông dụng | Kế thừa yêu cầu sửa chữa PMMS → giao task → QTTB xử lý trong khả năng, phối hợp KHTC&ĐT mua vật tư/thay thế nếu cần; ghi can thiệp, vật tư và kết quả trên thiết bị/yêu cầu đang có. |
| Sửa chữa chuyên dụng | QTTB làm trung gian theo dõi KHTC&ĐT, đơn vị yêu cầu và bên cung cấp chuyên môn. Ghi tiến độ/kết quả, bàn giao hoặc nhận lại nếu có; không mặc định nhân viên QTTB tự thực hiện chuyên môn. |
| Mua dịch vụ sửa chữa/bảo trì/triển khai | Theo dõi yêu cầu và việc tiếp nhận kết quả dịch vụ; không bắt có một lần nhập thiết bị vật lý nếu không phát sinh hàng hóa. |

“Chuyên dụng” xác định vai trò phối hợp và chuyên môn, không đồng nghĩa luôn mang thiết bị ra ngoài trường. Chuyên gia có thể đến xử lý tại chỗ; chỉ lập chứng từ xuất tạm/nhận lại khi thực tế có di chuyển. Mặc định ép `External` hoặc yêu cầu lý do ngoại lệ chỉ vì chọn `OnSite` không được suy ra từ tài liệu nguồn; phần code đang làm vậy phải được rà soát lại.

## 3. Hai nhóm kho và đơn vị quản lý

| Nhóm | Đơn vị theo dõi | Quy tắc |
| --- | --- | --- |
| Thiết bị sử dụng | Một bộ/thiết bị theo hợp đồng, một mã và một mã vạch ổn định | `quantity=0` khi hồ sơ chưa tiếp nhận, `quantity=1` sau tiếp nhận. Không gộp nhiều laptop thành một mã số lượng nhiều. |
| Vật tư dụng cụ | Loại vật tư/dụng cụ và đơn vị tính | Có nhiều đơn vị tồn; ghi nhập, xuất, thu hồi/trả kho và điều chỉnh bằng chứng từ. Xuất phục vụ công việc phải liên kết chứng từ/phiếu và thiết bị được xử lý nếu có. |

“Kho toàn trường” bao gồm thiết bị QTTB đang giữ và thiết bị đã cấp phát cho các đơn vị. Cấp phát/thu hồi nội bộ không làm tăng/giảm tổng thiết bị của trường. Xuất tạm đi sửa chữa cần theo dõi vị trí bên ngoài và nhập lại, không được coi là thanh lý hoặc mua mới khi nhận lại. Xuất khỏi trường vĩnh viễn phải có trạng thái vòng đời và chứng từ riêng; không dùng riêng số lượng 0 để suy ra thiết bị chưa từng tiếp nhận.

Mỗi hồ sơ phải phân biệt: đơn vị đề xuất ban đầu; đơn vị hiện chịu trách nhiệm; vị trí thực tế; người phụ trách. QTTB giữ chờ cấp phát/giữ sau thu hồi khác với QTTB sử dụng cho công việc của chính phòng. Trường hợp QTTB tự sử dụng cần ghi nhận đưa vào sử dụng trong phòng, không tạo điều chuyển có nguồn và đích trùng nhau.

Vật tư sửa chữa/thay thế như RAM, bóng đèn thuộc nhóm vật tư dụng cụ. Giấy, mực in tiêu hao thường ngày nằm ngoài phạm vi theo PMQTTB gốc. Danh mục chi tiết và đơn vị tính sẽ được chốt ở task kho vật tư.

Vật tư nhập về chỉ ghi nhận phòng QTTB đang giữ, không yêu cầu người dùng chia kho/kệ/ngăn. Tab Danh mục cho thêm loại vật tư/dụng cụ và đơn vị tính; nhập/xuất dùng chung mã danh mục, có thể tạo nhanh loại mới khi nhập. Hai danh mục này không có cột hoặc ô quản lý trạng thái trên giao diện. Các bộ chọn dùng dropdown, đơn vị tính hiển thị tên; mã vẫn dùng để liên kết bên trong.

## 4. Tiếp nhận, cấp phát và vòng đời thiết bị

### Mua mới

1. Đơn vị lập yêu cầu tại PMMS; PMQTTB kế thừa và liên kết task để quản lý phân công nhân viên. Nhân viên được giao kiểm tra tồn đối với hàng thông dụng hoặc theo dõi phối hợp đối với hàng chuyên dụng.
2. Nếu cần mua: phối hợp KHTC&ĐT. Khi tiếp nhận hàng, ghi phiếu nhập gắn đúng yêu cầu/task; khai báo từng thiết bị và lịch bảo trì từ hợp đồng nếu có. Không lập lại yêu cầu mua sắm trên PMQTTB.
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

Một yêu cầu PMMS có thể có nhiều dòng và nhiều chứng từ thực hiện cần thiết. Phải phân biệt yêu cầu nguồn, task phụ trách và chứng từ phát sinh. Mỗi kết quả/chứng từ liên kết về đúng yêu cầu, dòng nguồn, thiết bị và task/phần việc; một thiết bị có thể xuất hiện trong nhiều yêu cầu theo thời gian. Liên kết báo hỏng là tùy chọn; báo hỏng hoặc nhật ký kỹ thuật không thay cho yêu cầu sửa chữa nguồn tại PMMS.

Tài liệu nguồn yêu cầu tất cả nghiệp vụ có nhân viên QTTB phụ trách và có báo cáo theo từng nhân viên/toàn phòng. Module phân công do đồng đội cung cấp, nhưng tích hợp module đó là yêu cầu bắt buộc của luồng PMQTTB, không phải chức năng phụ. Không khôi phục module task cũ để tạo hệ thống song song.

Các quyết định đã xác nhận về phân công vẫn là đích tích hợp: tự tạo/liên kết việc chưa phân công khi có yêu cầu hoặc nghiệp vụ; quản lý giao nhân viên sau; chỉ nhân viên đang được giao được thực hiện; quản lý duyệt theo quyền; nhân viên được giao xác nhận hoàn tất khi đủ kết quả. Những nghiệp vụ nội bộ như kiểm kê, thu hồi, cấp phát và bảo trì cũng cần liên kết trách nhiệm, nhưng không vì vậy mà bắt chúng có một yêu cầu PMMS giả. Cách ánh xạ yêu cầu/task/phần việc dùng hợp đồng của module đồng đội, tránh tạo việc trùng.

Hiện code các phiếu chủ yếu kiểm tra vai trò, chưa có liên kết/chặn quyền theo người được giao; đó là phần thiếu cần sửa, không phải quy tắc đích. Phân người trong đợt kiểm kê đã có nhưng chưa thay cho phân công và báo cáo toàn bộ nghiệp vụ.

Vật tư và lịch sử can thiệp liên kết với chứng từ nhập/xuất, bản ghi thực hiện, thiết bị và task/yêu cầu tương ứng. Người thực hiện thực tế, người được giao phụ trách và người ghi nhận cần được phân biệt. Một chứng từ hoàn tất chưa đồng nghĩa task/yêu cầu đã hoàn tất; cần đủ kết quả toàn bộ phạm vi.

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
| Mã vạch | In Code 128 từ barcode đã lưu, tem 100 × 50 mm, in lại/PDF/tra cứu có kiểm chứng | CV-03 đã triển khai phần mềm; còn nghiệm thu máy in/máy quét vật lý. [Hướng dẫn](barcode-labels.md) |
| Thiết bị theo bộ | Tham chiếu cũ chỉ đọc ở API/DB/UI; chọn/khóa/mượn/điều chuyển độc lập, giữ phiếu và lịch sử cũ | CV-02 đã triển khai; [quy tắc](device-reference-policy.md) |
| Nhập rồi cấp phát | Tách phòng đề xuất và QTTB tiếp nhận; cấp phát/thực nhận và tự dùng riêng, giữ nghĩa phiếu cũ | CV-05 đã kiểm chứng sau CV-02; [hướng dẫn](receipt-custody-release.md) |
| Yêu cầu PMMS và xử lý sửa chữa | Có adapter/sự kiện/phiên bản, chứng từ và bằng chứng; UI còn tạo yêu cầu giả lập trong màn tác nghiệp và bắt lập phiếu sửa chữa cục bộ, chưa có task | **CV-04 cần sửa luồng**: nguồn PMMS → task → thực hiện/ghi nhận; tách công cụ giả lập, bỏ yêu cầu sửa chữa trùng, sửa cách hiểu chuyên dụng. [Hướng dẫn hiện trạng](pmms-simulation.md) |
| Phân công nhân viên và kiểm kê | Task chung chưa tích hợp; kiểm kê đã có phân người riêng. Việc gỡ module cũ không đáp ứng yêu cầu giao việc toàn phần mềm | **CV-11 là P0**, phối hợp module đồng đội để giao việc, kiểm soát người thực hiện và báo cáo toàn phòng; không xây hệ thống thứ hai |
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
7. Đơn vị lập yêu cầu mua/sửa ở PMMS; PMQTTB kế thừa và tạo/liên kết task chưa phân công, không buộc lập lại yêu cầu. Gửi lại sự kiện không tạo task thứ hai; quản lý giao nhân viên, người khác bị chặn thực hiện nhưng quản lý vẫn duyệt theo quyền.
8. Nghiệm thu đủ các nhánh trong bảng tình huống nguồn: thông dụng đủ/thiếu tồn, mua chuyên dụng, sửa thông dụng và theo dõi sửa chuyên dụng. Chuyên dụng xử lý tại chỗ không bị hiểu thành QTTB tự có chuyên môn hoặc buộc xuất khỏi trường.
9. Kiểm kê/thu hồi/cấp phát/bảo trì có nhân viên phụ trách và kết quả đưa vào báo cáo theo nhân viên/toàn phòng, không tạo yêu cầu PMMS giả để hợp thức hóa việc nội bộ.

Các tình huống là tiêu chí nghiệm thu. Trạng thái kỹ thuật đã kiểm thử và phần còn sai/thiếu phải được đọc cùng mục 8; chưa coi CV-04/CV-11 hoàn tất theo tài liệu nguồn.
