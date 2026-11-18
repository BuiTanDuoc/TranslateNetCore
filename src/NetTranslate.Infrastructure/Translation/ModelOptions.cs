namespace NetTranslate.Infrastructure.Translation;

/// <summary>Cấu hình engine ONNX, section "Models" trong appsettings.json.</summary>
public sealed class ModelOptions
{
    public const string SectionName = "Models";

    /// <summary>Thư mục chứa các model, mỗi cặp ngôn ngữ một thư mục con (vd. models/en-vi).</summary>
    public string Path { get; set; } = "models";

    /// <summary>Nạp tất cả model ngay khi khởi động (mặc định: nạp khi dùng lần đầu).</summary>
    public bool Preload { get; set; }

    /// <summary>Ưu tiên file *_quantized.onnx (nhỏ, nhanh hơn, chất lượng giảm nhẹ).</summary>
    public bool PreferQuantized { get; set; }

    /// <summary>Số câu tối đa trong một batch gửi vào model.</summary>
    public int BatchSize { get; set; } = 8;

    /// <summary>Số luồng intra-op cho ONNX Runtime. 0 = để ORT tự chọn.</summary>
    public int IntraOpThreads { get; set; }

    /// <summary>Số batch được chạy đồng thời trên toàn server. 0 = số CPU / 2.</summary>
    public int MaxConcurrency { get; set; }
}
