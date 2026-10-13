# ScreenOcr — Chụp màn hình → Text (offline)

App portable, rất nhẹ (~1 MB), dùng engine OCR có sẵn của Windows 10/11 (`Windows.Media.Ocr`).
Tối ưu cho việc chụp **code trong Eclipse**: giữ thụt lề, bỏ số dòng, giữ dòng trống, hỗ trợ comment tiếng Nhật.

## 0. Tải bản build sẵn (không cần cài gì)

Vào trang **Releases** của repo → tải `ScreenOcr-x.y.z.zip`.

1. Chuột phải file zip → **Properties** → tick **Unblock** → OK (tránh Windows chặn file tải từ internet).
2. Giải nén vào thư mục bất kỳ → chạy `ScreenOcr.exe`.
3. Lần đầu chạy, nếu SmartScreen báo *"Windows protected your PC"* → **More info** → **Run anyway**
   (vì exe chưa ký số).

Tạo bản phát hành mới: push một tag dạng `v1.0.1`, GitHub Actions sẽ tự build và đính kèm file zip vào Release.

## 1. Build

**Yêu cầu:** Windows 10/11, Visual Studio 2022 (workload *.NET desktop development*) **hoặc** .NET SDK 8 trở lên.

- **Cách 1 – Visual Studio:** mở `ScreenOcr.csproj` → chọn `Release` → Build (Ctrl+Shift+B).
- **Cách 2 – Dòng lệnh:** chạy `build.bat` (hoặc `dotnet build -c Release`).

Kết quả nằm ở `bin\Release\net48\`. **Copy cả thư mục này** sang máy khác là chạy được
(.NET Framework 4.8 đã có sẵn trên Windows 10/11, không cần cài gì thêm).

> Lần build đầu cần internet để NuGet tải 2 package: `Microsoft.Windows.SDK.Contracts`
> (để gọi Windows OCR) và `Microsoft.NETFramework.ReferenceAssemblies`. App chạy thì hoàn toàn offline.

## 2. Gói ngôn ngữ OCR

Kiểm tra các gói đã cài (PowerShell):

```powershell
Get-WindowsCapability -Online | Where-Object Name -like 'Language.OCR*'
```

Cài thêm nếu thiếu (PowerShell **Run as Administrator**):

```powershell
Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"
Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"
```

## 3. Sử dụng

| Thao tác | Cách làm |
|---|---|
| Chụp vùng màn hình | Hotkey **Ctrl+Shift+Q** (ở bất kỳ đâu) hoặc nút 📷. Kéo chuột chọn vùng, Esc/chuột phải để huỷ |
| Chụp lại vùng trước | Hotkey **Ctrl+Alt+Q** hoặc nút 🔁: chụp đúng vùng lần trước, không cần kéo chọn (tiện khi cuộn code rồi chụp tiếp) |
| Chụp nhanh | Tick *Chụp nhanh*: khi app đang ẩn ở khay, nhấn hotkey → OCR → copy luôn, chỉ hiện thông báo, không mở cửa sổ |
| Tự OCR từ clipboard | Tick *Tự OCR ảnh trong clipboard*: chụp bằng **Win+Shift+S** là tự có text trong clipboard |
| Nhập ảnh | Nút 📂 (chọn nhiều file), **kéo thả** file vào cửa sổ, hoặc **Ctrl+V** dán ảnh (VD sau khi chụp bằng Win+Shift+S) |
| OCR | Tự chạy sau khi chụp. Hoặc **F5** = OCR ảnh đang chọn, **Ctrl+F5** = OCR tất cả |
| Copy | Tự copy sau OCR, hoặc nút ⧉ / **Ctrl+Shift+C** |
| Lưu 1 file | 💾 / **Ctrl+S** → chọn thư mục + đặt tên file (.txt / .java) |
| Xuất nhiều file | 🗂 → chọn thư mục, mỗi ảnh ra 1 file `.txt` (UTF-8) |
| Chọn nhiều ảnh | Ctrl/Shift + click, Ctrl+A. Khung text sẽ hiển thị gộp kết quả |
| Ghép ảnh chụp cuộn | Tick *Ghép liền nhiều ảnh*: chọn nhiều ảnh chụp liên tiếp → ghép thành 1 đoạn, tự bỏ các dòng trùng ở chỗ nối |
| Sửa kết quả | Chọn 1 ảnh → sửa trực tiếp trong khung text trước khi copy/lưu |
| Đổi hotkey | Click ô *Hotkey* / *Chụp lại* ở thanh tuỳ chọn → nhấn tổ hợp phím mới |
| Thoát hẳn | Nút X chỉ thu xuống khay hệ thống. Chuột phải icon ở khay → **Thoát** |

Cài đặt được lưu vào `ScreenOcr.ini` cạnh file exe (nếu thư mục không ghi được thì lưu vào `%APPDATA%\ScreenOcr`).

## 4. Mẹo để OCR code Eclipse chính xác hơn

- **Ngôn ngữ:** code có comment tiếng Nhật → chọn `English + 日本語 (hybrid)` (cần cài cả 2 gói OCR):
  app chạy cả 2 engine rồi ghép lại — chữ Nhật lấy từ engine `ja`, code/ký hiệu `{ } ; ( )` lấy từ engine `en`.
  Code thuần tiếng Anh → `English (en-US)` (nhanh hơn).
- **Phóng to font Eclipse** (Ctrl + `=`) trước khi chụp; chữ càng to OCR càng chuẩn. Tuỳ chọn *Tự phóng to* (bật sẵn)
  sẽ đo cỡ chữ sau lần OCR đầu, nếu chữ còn nhỏ thì phóng to thêm (tối đa ×6) và OCR lại.
- **Tắt "Show Whitespace Characters"** của Eclipse (nút ¶ trên thanh công cụ) khi chụp: các ký hiệu `^ » ↵` làm OCR mất dấu `;` `)`.
  Tuỳ chọn *Làm sạch nền* (bật sẵn) đã tự xoá phần lớn ký hiệu mờ này và nền highlight dòng hiện tại.
- Chụp **gọn vùng code**, tránh dính thanh cuộn, icon breakpoint, dấu gập code (+/−).
- *Bỏ số dòng* chỉ kích hoạt khi đa số dòng bắt đầu bằng số, nên để bật cũng an toàn.
- *Thụt lề* được làm tròn theo bội số 4 cột (cấu hình `TabSize` trong file ini) vì Eclipse thụt lề theo tab.
- Eclipse **Dark theme**: để bật *Tự đảo màu nền tối*.
- Các lỗi OCR hay gặp với code: `l` ↔ `1` ↔ `|`, `O` ↔ `0`, `;` ↔ `:`. Tuỳ chọn *Sửa lỗi OCR code* tự sửa các trường hợp chắc chắn:
  `1O0` → `100`, `equa1s` → `equals`, `StringUti1` → `StringUtil`, `.DOCUMENT TYPE Ie3` → `.DOCUMENT_TYPE_103`,
  `foo() .bar()` → `foo().bar()`, `int x = 0:` → `int x = 0;`, thêm `;` / `)` bị mất cuối câu lệnh.
  Không đụng vào chuỗi, comment, `case x:`, label, code Python.
  Vẫn nên xem lại nhanh trước khi dùng.

## 5. Cấu trúc mã nguồn

| File | Vai trò |
|---|---|
| `Program.cs` | Điểm vào, chống chạy 2 instance |
| `MainForm.cs` | Giao diện, khay hệ thống, hotkey, nhập/xuất |
| `RegionSelectForm.cs` | Màn hình chọn vùng (hỗ trợ nhiều màn hình, DPI 125%/150%) |
| `OcrService.cs` | Tiền xử lý ảnh + gọi Windows OCR |
| `TextLayout.cs` | Dựng lại text dạng code từ toạ độ chữ (thụt lề, dòng trống, số dòng), ghép en + ja, ghép ảnh cuộn |
| `CodeFixer.cs` | Sửa lỗi OCR hay gặp với code bằng quy tắc |
| `AppSettings.cs` | Đọc/ghi `ScreenOcr.ini` |
| `NativeMethods.cs` | Win32 `RegisterHotKey`, parse hotkey |

## 6. Unit test

Phần xử lý text (`TextLayout`, `CodeFixer`) không phụ thuộc Windows nên test chạy được cả trên Linux/macOS:

```
dotnet test tests/ScreenOcr.Tests/ScreenOcr.Tests.csproj
```

GitHub Actions chạy test này trước mỗi lần build.
