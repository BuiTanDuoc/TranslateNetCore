namespace NetTranslate.Domain.Entities;

/// <summary>
/// API key cấp cho client. Tương đương bảng api_keys của LibreTranslate.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Giá trị key client gửi lên qua tham số api_key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Tên gợi nhớ (khách hàng, ứng dụng...).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Số request tối đa mỗi phút. 0 = không giới hạn.</summary>
    public int ReqLimit { get; set; } = 60;

    /// <summary>Số ký tự tối đa mỗi request. null = dùng giới hạn chung của server.</summary>
    public int? CharLimit { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }
}
