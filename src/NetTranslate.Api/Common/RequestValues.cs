using System.Text.Json;

namespace NetTranslate.Api.Common;

/// <summary>
/// Đọc tham số request theo cách của LibreTranslate: chấp nhận JSON body,
/// form (urlencoded/multipart) hoặc query string. Tham số "q" có thể là chuỗi hoặc mảng.
/// </summary>
public sealed class RequestValues
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _arrays = new(StringComparer.OrdinalIgnoreCase);

    public IFormFileCollection? Files { get; private set; }

    public static async Task<RequestValues> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        var result = new RequestValues();

        if (request.HasJsonContentType())
        {
            using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new BadHttpRequestException("Invalid request: JSON body must be an object");

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    result._arrays.Add(prop.Name);
                    result._values[prop.Name] = prop.Value.EnumerateArray().Select(ToText).ToList();
                }
                else if (prop.Value.ValueKind != JsonValueKind.Null)
                {
                    result._values[prop.Name] = [ToText(prop.Value)];
                }
            }
        }
        else if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            foreach (var (key, value) in form)
            {
                result._values[key] = value.Where(v => v is not null).Select(v => v!).ToList();
                if (value.Count > 1) result._arrays.Add(key);
            }
            result.Files = form.Files;
        }

        // Query string bổ sung (không ghi đè body)
        foreach (var (key, value) in request.Query)
            result._values.TryAdd(key, value.Where(v => v is not null).Select(v => v!).ToList());

        return result;
    }

    private static string ToText(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => e.GetRawText(),
    };

    public string? Get(string name) =>
        _values.TryGetValue(name, out var list) && list.Count > 0 ? list[0] : null;

    public IReadOnlyList<string> GetList(string name) =>
        _values.TryGetValue(name, out var list) ? list : [];

    public bool IsArray(string name) => _arrays.Contains(name);

    public int GetInt(string name, int fallback = 0) =>
        int.TryParse(Get(name), out var v) ? v : fallback;
}
