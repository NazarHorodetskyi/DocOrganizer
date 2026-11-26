using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocOrganizer.Api.Contracts;

namespace DocOrganizer.Api.Services;

/// <summary>
/// Експорт списку документів. Кілька реалізацій зареєстровано як keyed services ("csv", "json");
/// контролер обирає реалізацію за форматом із маршруту під час виконання.
/// </summary>
public interface IDocumentExporter
{
    string ContentType { get; }
    string Extension { get; }
    byte[] Export(IReadOnlyList<DocumentListItemDto> documents);
}

/// <summary>CSV (UTF-8 з BOM, щоб Excel коректно показав кирилицю). Без стану — Singleton.</summary>
public sealed class CsvDocumentExporter : IDocumentExporter
{
    public string ContentType => "text/csv";
    public string Extension => "csv";

    public byte[] Export(IReadOnlyList<DocumentListItemDto> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id;Owner;Type;Number;Title;ExpiryDate;Status;ExpiryState;DaysLeft");
        foreach (var d in documents)
        {
            sb.Append(d.Id).Append(';')
              .Append(Escape(d.OwnerFullName)).Append(';')
              .Append(Escape(d.DocumentTypeName)).Append(';')
              .Append(Escape(d.Number)).Append(';')
              .Append(Escape(d.Title)).Append(';')
              .Append(d.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(';')
              .Append(d.Status).Append(';')
              .Append(d.ExpiryState).Append(';')
              .Append(d.DaysLeft)
              .AppendLine();
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Escape(string value)
        => value.IndexOfAny([';', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

/// <summary>JSON-масив документів. Без стану — Singleton.</summary>
public sealed class JsonDocumentExporter : IDocumentExporter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ContentType => "application/json";
    public string Extension => "json";

    public byte[] Export(IReadOnlyList<DocumentListItemDto> documents)
        => JsonSerializer.SerializeToUtf8Bytes(documents, Options);
}
