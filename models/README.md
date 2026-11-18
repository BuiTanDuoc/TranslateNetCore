# Thư mục model

Mỗi cặp ngôn ngữ là một thư mục con. Tên thư mục `<nguồn>-<đích>` (vd. `en-vi`) hoặc có file `model.json`.

```
models/
├── en-vi/
│   ├── encoder_model.onnx
│   ├── decoder_model.onnx
│   ├── decoder_with_past_model.onnx   (tuỳ chọn – bật KV-cache, nhanh hơn nhiều)
│   ├── source.spm
│   ├── vocab.json
│   ├── config.json
│   └── model.json                      (tuỳ chọn)
└── vi-en/ ...
```

Cách nhanh nhất: trang **Quản trị → Model → Thêm model từ Hugging Face**. Nhập cặp ngôn ngữ (vd. `ja-en`),
server sẽ tải file ONNX có sẵn từ repo `Xenova/opus-mt-<cặp>` (hoặc repo bạn nhập), kiểm tra kích thước, SHA-256,
nội dung (không phải trang HTML), rồi cài vào `models/<cặp>` và dịch thử một câu. Tương đương qua API:

```bash
curl -X POST http://localhost:5000/admin/models/download -H "X-Admin-Token: <token>" \
     -H "Content-Type: application/json" -d '{"pair":"ja-en","quantized":true}'
curl http://localhost:5000/admin/models/downloads -H "X-Admin-Token: <token>"   # xem tiến trình
```

Repo private/gated: đặt `Models:HuggingFaceToken`. Dùng mirror: đặt `Models:HuggingFaceUrl`.

Nếu tự tải tay từ Hugging Face, hãy dùng link `/resolve/main/...` chứ không phải `/blob/main/...` (link `/blob/` trả về trang HTML).

Hoặc tự export bằng script:

```bash
pip install -r scripts/requirements.txt
python scripts/download_models.py en-vi vi-en
```

`model.json` (tuỳ chọn):

```json
{ "source": "en", "target": "vi", "targetPrefix": null }
```

`targetPrefix` dùng cho model đa ngôn ngữ đích như `opus-mt-en-mul` (vd. `">>vie<<"`).
