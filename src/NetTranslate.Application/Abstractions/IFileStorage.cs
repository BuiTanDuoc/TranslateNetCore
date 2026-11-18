namespace NetTranslate.Application.Abstractions;

public sealed record StoredFile(string Id, string FileName, string FullPath);

/// <summary>Lưu tạm file đã dịch để client tải về qua /download_file/{id}.</summary>
public interface IFileStorage
{
    /// <summary>Tạo file mới, trả về thông tin và stream ghi.</summary>
    (StoredFile File, Stream Stream) Create(string fileName);

    StoredFile? Find(string id);

    /// <summary>Xoá các file cũ hơn <paramref name="maxAge"/>.</summary>
    int Cleanup(TimeSpan maxAge);
}
