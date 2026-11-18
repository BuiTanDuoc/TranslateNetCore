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

Tạo bằng script:

```bash
pip install -r scripts/requirements.txt
python scripts/download_models.py en-vi vi-en
```

`model.json` (tuỳ chọn):

```json
{ "source": "en", "target": "vi", "targetPrefix": null }
```

`targetPrefix` dùng cho model đa ngôn ngữ đích như `opus-mt-en-mul` (vd. `">>vie<<"`).
