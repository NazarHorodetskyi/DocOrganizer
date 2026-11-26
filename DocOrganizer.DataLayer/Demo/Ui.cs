using DocOrganizer.DataLayer.DataAccess;
using DocOrganizer.DataLayer.Domain;

namespace DocOrganizer.DataLayer.Demo;

/// <summary>Дрібні помічники для виводу в консоль.</summary>
internal static class Ui
{
    public static void Header(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 100));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 100));
        Console.ResetColor();
    }

    public static void Step(string text) => Write(ConsoleColor.Yellow, $"\n>> {text}");
    public static void Ok(string text) => Write(ConsoleColor.Green, $"   [OK] {text}");
    public static void Warn(string text) => Write(ConsoleColor.DarkYellow, $"   [!] {text}");
    public static void Error(string text) => Write(ConsoleColor.Red, $"   [ПОМИЛКА] {text}");
    public static void Info(string text) => Console.WriteLine($"   {text}");

    public static void Pause()
    {
        Console.WriteLine();
        Console.Write("Натисніть Enter, щоб повернутися до меню...");
        Console.ReadLine();
    }

    public static string Ask(string prompt)
    {
        Console.Write($"   {prompt}");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    public static string Fmt(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "—";

    public static string Cut(string? text, int max)
        => text is null ? string.Empty : text.Length <= max ? text : text[..(max - 1)] + "…";

    public static void PrintDocuments(IReadOnlyCollection<DocumentListItemDto> documents, DateOnly today)
    {
        Console.WriteLine($"   {"Id",4} | {"Назва",-34} | {"Номер",-15} | {"Власник",-16} | {"Тип",-20} | {"Діє до",-10} | Стан");
        Console.WriteLine("   " + new string('-', 125));
        foreach (var d in documents)
        {
            Console.WriteLine(
                $"   {d.Id,4} | {Cut(d.Title, 34),-34} | {Cut(d.Number, 15),-15} | {Cut(d.OwnerName, 16),-16} | " +
                $"{Cut(d.TypeName, 20),-20} | {Fmt(d.ExpiryDate),-10} | {ExpiryInfo.Describe(d.ExpiryDate, d.Status, today)}");
        }
        Console.WriteLine($"   Усього рядків: {documents.Count}");
    }

    public static void PrintDocument(DocumentDetailsDto? d, DateOnly today)
    {
        if (d is null)
        {
            Info("(документ не знайдено)");
            return;
        }

        Info($"#{d.Id} «{d.Title}», {d.TypeName} № {d.Number}");
        Info($"   власник: {d.OwnerName} (Id {d.OwnerId}); видано {Fmt(d.IssueDate)}; діє до {Fmt(d.ExpiryDate)}");
        Info($"   статус: {d.Status} → {ExpiryInfo.Describe(d.ExpiryDate, d.Status, today)}; файл: {d.FilePath ?? "—"}; примітка: {d.Notes ?? "—"}");
    }

    private static void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
