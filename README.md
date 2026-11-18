# NetTranslate

API dịch máy mã nguồn mở, chạy offline, viết bằng **ASP.NET Core .NET 10**. Ý tưởng và API giống
[LibreTranslate](https://github.com/LibreTranslate/LibreTranslate), nhưng engine dịch là model
**MarianMT (Helsinki-NLP OPUS-MT)** chạy bằng **ONNX Runtime**, không cần Python khi chạy server.

## Tính năng

| Tính năng | Endpoint |
|---|---|
| Dịch văn bản (text / HTML, `q` là chuỗi hoặc mảng) | `POST /translate` |
| Tự nhận diện ngôn ngữ (`source=auto`) | `POST /detect` |
| Danh sách ngôn ngữ | `GET /languages` |
| Dịch file `.txt .md .html .docx .pptx` | `POST /translate_file`, `GET /download_file/{id}` |
| Cấu hình cho UI | `GET /frontend/settings` |
| API key + giới hạn request/phút, ký tự, batch | tham số `api_key` |
| Quản trị key & model (header `X-Admin-Token`) | `/admin/keys`, `/admin/models` |
| Tài liệu API tương tác | `/scalar/v1`, `/openapi/v1.json` |
| Web UI React 19 (dark/light, vi/en) | `/` và `/#/admin` |

Pivot giống Argos Translate: thiếu model trực tiếp (vd. `fr → vi`) thì dịch `fr → en → vi`.

## Kiến trúc (Clean Architecture)

```
NetTranslate/
├── src/
│   ├── NetTranslate.Domain          Entity ApiKey, exception, bảng mã ngôn ngữ
│   ├── NetTranslate.Application     Interface + nghiệp vụ: TranslationService (pivot, tách câu,
│   │                                auto-detect), AccessGuard (key/rate limit), FileTranslationService
│   ├── NetTranslate.Infrastructure  ONNX MarianMT engine, SentencePiece C# thuần, bộ nhận diện ngôn ngữ,
│   │                                dịch HTML/DOCX/PPTX, EF Core SQLite, rate limiter, lưu file
│   └── NetTranslate.Api             Minimal API, middleware lỗi, phục vụ Web UI trong wwwroot
├── web/                             React 19 + Vite + TypeScript + Tailwind v4
├── scripts/                         Tải model: .ps1 / .sh (không cần Python) hoặc .py (tự export)
└── models/                          Mỗi cặp ngôn ngữ một thư mục (en-vi, vi-en, ...)
```

Muốn đổi engine (NLLB, gọi OpenAI...) chỉ cần viết class mới implement `ITranslationEngine`
và đăng ký trong `Infrastructure/DependencyInjection.cs`. Tương tự với `ILanguageDetector`.

## Chạy thử

Yêu cầu: .NET 10 SDK, Node 20+. Python không bắt buộc.

### 1. Tải model

**Cách A: không cần Python (khuyên dùng).** Tải bản ONNX đã export sẵn trên Hugging Face
(`Xenova/opus-mt-*`, `onnx-community/opus-mt-*`). Script tự lấy tokenizer từ `Helsinki-NLP/opus-mt-*` nếu thiếu.

```powershell
# Windows (PowerShell)
powershell -ExecutionPolicy Bypass -File .\scripts\download_models.ps1 en-vi vi-en
powershell -ExecutionPolicy Bypass -File .\scripts\download_models.ps1 en-vi vi-en -Quantized   # int8, ~150 MB/cặp
```

```bash
# macOS / Linux
./scripts/download_models.sh en-vi vi-en
QUANTIZED=1 ./scripts/download_models.sh en-vi vi-en
```

Bản fp32 khoảng 600 MB mỗi cặp. Bản `-Quantized` nhẹ hơn khoảng 4 lần, chất lượng giảm nhẹ.
Server tự dùng bản quantized nếu chỉ có bản đó, không cần sửa cấu hình.

Nếu cặp ngôn ngữ chưa có bản ONNX sẵn, script sẽ báo lỗi. Khi đó dùng cách B.

**Cách B: tự export bằng Python** (dùng được với mọi model OPUS-MT):

```bash
pip install -r scripts/requirements.txt
python scripts/download_models.py en-vi vi-en
```

**Tải tay:** vào `https://huggingface.co/Xenova/opus-mt-en-vi/tree/main` rồi tải `source.spm`, `vocab.json`,
`config.json` và trong thư mục `onnx/` tải `encoder_model.onnx`, `decoder_model.onnx`, `decoder_with_past_model.onnx`.
Bỏ tất cả vào `models/en-vi/`.

### 2. Chạy API

```bash
cd src/NetTranslate.Api
dotnet run            # http://localhost:5000, tài liệu API: http://localhost:5000/scalar/v1
```

### 3. Chạy Web UI (dev)

```bash
cd web
npm install
npm run dev           # http://localhost:5173, proxy API sang :5000
```

`npm run build` ghi bản build vào `src/NetTranslate.Api/wwwroot`, khi đó API phục vụ luôn UI ở `http://localhost:5000`.

### Docker

```bash
./scripts/download_models.sh en-vi vi-en     # hoặc download_models.ps1 trên Windows
docker compose up --build    # http://localhost:5000
```

## Ví dụ gọi API

```bash
curl -X POST http://localhost:5000/translate \
  -H "Content-Type: application/json" \
  -d '{"q":"Hello world!","source":"auto","target":"vi"}'
# {"translatedText":"Xin chào thế giới!","detectedLanguage":{"confidence":100,"language":"en"}}

curl -X POST http://localhost:5000/translate_file \
  -F file=@report.docx -F source=en -F target=vi
# {"translatedFileUrl":"http://localhost:5000/download_file/3f2a..."}
```

Client LibreTranslate có sẵn (Python `libretranslatepy`, JS...) dùng được bằng cách trỏ URL sang server này.
File `src/NetTranslate.Api/NetTranslate.Api.http` có thêm ví dụ cho VS / Rider / VS Code.

## Cấu hình (`appsettings.json` hoặc biến môi trường `Section__Key`)

| Khoá | Mặc định | Ý nghĩa |
|---|---|---|
| `Translate:CharLimit` | 5000 | Ký tự tối đa mỗi request (-1 = không giới hạn) |
| `Translate:ReqLimit` | 60 | Request/phút mỗi IP khi không có key (0 = không giới hạn) |
| `Translate:BatchLimit` | 50 | Số phần tử tối đa khi `q` là mảng |
| `Translate:ApiKeysEnabled` | true | Bật API key |
| `Translate:RequireApiKey` | false | Bắt buộc có key |
| `Translate:AllowWebUiWithoutKey` | true | Web UI đi kèm không cần key (vẫn bị giới hạn theo IP) |
| `Translate:PivotLanguage` | en | Ngôn ngữ trung gian |
| `Translate:DefaultSource/DefaultTarget` | auto / vi | Mặc định trên UI |
| `Translate:FileSizeLimitMb` | 10 | Dung lượng file tối đa |
| `Translate:FileRetentionMinutes` | 30 | Thời gian giữ file đã dịch |
| `Models:Path` | ../../models | Thư mục model |
| `Models:Preload` | false | Nạp tất cả model khi khởi động |
| `Models:PreferQuantized` | false | Ưu tiên `*_quantized.onnx` |
| `Models:BatchSize` | 8 | Số câu mỗi batch |
| `Models:MaxConcurrency` | 0 | Số batch chạy song song (0 = số CPU / 2) |
| `Admin:Token` | (trống) | Token quản trị; trống = tắt `/admin` |
| `TrustForwardedHeaders` | false | Bật khi chạy sau reverse proxy để lấy IP thật |

## Quản lý API key

Đặt `Admin:Token`, rồi vào `http://localhost:5000/#/admin`, hoặc gọi API:

```bash
curl -X POST http://localhost:5000/admin/keys -H "X-Admin-Token: <token>" \
  -H "Content-Type: application/json" -d '{"name":"App A","reqLimit":120,"charLimit":20000}'
```

## Ghi chú kỹ thuật

- **Tokenizer**: SentencePiece Unigram viết bằng C# (đọc thẳng `source.spm` dạng protobuf, chọn cách cắt
  bằng Viterbi), sau đó tra `vocab.json`. Không cần thư viện native.
- **Giải mã**: greedy theo batch. Có `decoder_with_past_model.onnx` thì dùng KV-cache (nhanh hơn nhiều với câu dài).
  Không hỗ trợ `decoder_model_merged.onnx`: hãy export với `--no-post-process` (script đã làm sẵn).
- **Nhận diện ngôn ngữ**: heuristic theo hệ chữ Unicode, ký tự đặc trưng và từ dừng, không cần model.
  Muốn chính xác hơn với văn bản ngắn chữ Latin, có thể thay bằng fastText `lid.176` qua `ILanguageDetector`.
- **DOCX/PPTX**: dịch theo từng đoạn. Nếu trong một đoạn có nhiều kiểu định dạng (vd. một từ in đậm),
  cả đoạn sẽ lấy định dạng của run đầu tiên.
- **Rate limit** lưu trong RAM (một instance). Chạy nhiều instance thì thay `IRateLimiter` bằng bản dùng Redis.

## Chưa có (so với LibreTranslate)

- `alternatives` (luôn trả mảng rỗng) — cần beam search
- `/suggest` (trả 403)
- Dịch PDF, ODT, EPUB

## Publish lên IIS
1. Cài ASP.NET Core Hosting Bundle 10 lên server, sau đó chạy iisreset.

2. Build trên máy dev:
```
cd web; 
npm ci; 
npm run build; 
cd ..
dotnet publish src/NetTranslate.Api/NetTranslate.Api.csproj -c Release -r win-x64 --self-contained -o publish/iis
```
3. Copy lên server
- Copy publish/iis vào site directory, 
- copy thư mục models vào <site directory>\data\models.
- Sau đó tạo file appsettings.Production.json để khai báo đường dẫn DB, models, files và Admin token.
4. Tạo app pool NetTranslate (No Managed Code, AlwaysRunning, idle timeout = 0), rồi tạo site NetTranslate với binding cho translate.adgps.vn. Thêm một binding nữa cho translateapi.adgps.vn trên cùng site đó.
5. Cấp quyền ghi cho app pool:
```	
	icacls C:\inetpub\nettranslate-data /grant "IIS AppPool\NetTranslate:(OI)(CI)M"
```
6. Trỏ DNS của 2 tên miền về server, binding.
7. Kiểm tra lần lượt https://translate.adgps.vn, https://translateapi.adgps.vn/languages và https://translateapi.adgps.vn/scalar/v1.