namespace NetTranslate.Application.Options;

/// <summary>
/// Cấu hình chung, tương đương các tham số dòng lệnh của LibreTranslate
/// (--char-limit, --req-limit, --batch-limit, --api-keys, --require-api-key...).
/// Đọc từ section "Translate" trong appsettings.json.
/// </summary>
public sealed class TranslateOptions
{
    public const string SectionName = "Translate";

    /// <summary>Số ký tự tối đa mỗi request. -1 = không giới hạn.</summary>
    public int CharLimit { get; set; } = 5000;

    /// <summary>Số request tối đa mỗi phút cho mỗi IP (khi không dùng API key). 0 = không giới hạn.</summary>
    public int ReqLimit { get; set; } = 60;

    /// <summary>Số phần tử tối đa khi q là mảng. -1 = không giới hạn.</summary>
    public int BatchLimit { get; set; } = 50;

    /// <summary>Bật cơ chế API key.</summary>
    public bool ApiKeysEnabled { get; set; } = true;

    /// <summary>Bắt buộc phải có API key mới được gọi API.</summary>
    public bool RequireApiKey { get; set; }

    /// <summary>
    /// Khi RequireApiKey = true, cho phép request từ chính Web UI (cùng origin) không cần key.
    /// </summary>
    public bool AllowWebUiWithoutKey { get; set; } = true;

    /// <summary>Ngôn ngữ trung gian khi không có model dịch trực tiếp.</summary>
    public string PivotLanguage { get; set; } = "en";

    public string DefaultSource { get; set; } = "auto";
    public string DefaultTarget { get; set; } = "vi";

    public bool DisableFilesTranslation { get; set; }

    /// <summary>Dung lượng file tối đa (MB).</summary>
    public int FileSizeLimitMb { get; set; } = 10;

    /// <summary>File đã dịch được giữ lại bao lâu (phút).</summary>
    public int FileRetentionMinutes { get; set; } = 30;
}
