using DocOrganizer.Api.Domain;
using Microsoft.EntityFrameworkCore;
using static DocOrganizer.Api.Domain.DocumentTypeIds;
using static DocOrganizer.Api.Domain.TagIds;

namespace DocOrganizer.Api.Data;

/// <summary>
/// Ідентифікатори (claim sub) користувачів Auth0, яким належать тестові дані.
/// Auth0 генерує sub під час створення користувача (вигляд "auth0|65f0c1..."), тому їх треба
/// скопіювати з консолі Auth0 (User Management → Users → user_id) в appsettings.json, секція "Seed".
/// </summary>
public sealed class SeedOptions
{
    public const string Section = "Seed";

    /// <summary>sub користувача olena (роль member): родина Коваленків — 3 профілі, 13 документів.</summary>
    public string MemberAccountId { get; set; } = "";

    /// <summary>sub користувача petro (роль member): 2 профілі + профіль із документами для навантажувального тесту.</summary>
    public string SecondMemberAccountId { get; set; } = "";

    /// <summary>sub користувача viewer (роль viewer): 1 профіль, 4 документи.</summary>
    public string ViewerAccountId { get; set; } = "";

    /// <summary>Скільки згенерованих документів створити для навантажувального тесту (0 — не створювати).</summary>
    public int LoadTestDocuments { get; set; } = 5000;
}

/// <summary>
/// Тестові дані (UseSeeding/UseAsyncSeeding). Довідники DocumentTypes/Tags заповнює HasData у міграції,
/// тут — власники, документи, нагадування, мітки. Дати рахуються від сьогодні, тому завжди є
/// прострочені, «скоро спливають» і чинні документи.
/// </summary>
public static class SeedData
{
    public static void Seed(DbContext context, SeedOptions options)
    {
        if (context.Set<Owner>().Any()) return;   // ідемпотентність
        context.AddRange(BuildOwners(DateOnly.FromDateTime(DateTime.Today), options));
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, SeedOptions options, CancellationToken ct)
    {
        if (await context.Set<Owner>().AnyAsync(ct)) return;
        context.AddRange(BuildOwners(DateOnly.FromDateTime(DateTime.Today), options));
        await context.SaveChangesAsync(ct);
    }

    private static List<Owner> BuildOwners(DateOnly today, SeedOptions o)
    {
        var channelIndex = 0;

        Document Doc(int typeId, string number, string title, int issuedDaysAgo, int? expiresInDays,
                     int[] tagIds, int[] reminderDays, DocumentStatus status = DocumentStatus.Active)
        {
            DateOnly? expiry = expiresInDays is null ? null : today.AddDays(expiresInDays.Value);
            var document = new Document
            {
                DocumentTypeId = typeId,
                Number = number,
                Title = title,
                IssueDate = today.AddDays(-issuedDaysAgo),
                ExpiryDate = expiry,
                Status = status,
                FilePath = $"files/{number}.pdf"
            };

            foreach (var tagId in tagIds)
                document.DocumentTags.Add(new DocumentTag { TagId = tagId });

            if (expiry is not null)
            {
                foreach (var days in reminderDays)
                {
                    var remindOn = expiry.Value.AddDays(-days);
                    var sent = remindOn < today;
                    document.Reminders.Add(new Reminder
                    {
                        DaysBefore = days,
                        RemindOn = remindOn,
                        Channel = (ReminderChannel)(channelIndex++ % 3),
                        IsSent = sent,
                        SentAt = sent ? remindOn.ToDateTime(new TimeOnly(9, 0)) : null
                    });
                }
            }
            return document;
        }

        var owners = new List<Owner>
        {
            // ── Акаунт olena (member): веде профілі всієї родини Коваленків ──
            new()
            {
                AccountId = o.MemberAccountId,
                FullName = "Олена Коваленко", Email = "olena.kovalenko@example.com", BirthDate = new DateOnly(1987, 9, 3),
                Documents =
                [
                    Doc(IdCard, "004598765", "ID-картка Олени", 1500, 2150, [Personal], [60]),
                    Doc(ForeignPassport, "FB654321", "Закордонний паспорт Олени", 3000, 650, [Personal, Travel], [90]),
                    Doc(Visa, "SCH-77889900", "Шенгенська віза", 200, -15, [Travel], [30, 7]),
                    Doc(Certificate, "CERT-IELTS-2291", "Сертифікат IELTS", 600, 130, [Work], [30]),
                    Doc(Contract, "DOG-2024-118", "Договір оренди квартири", 400, 330, [Finance], [30, 60])
                ]
            },
            new()
            {
                AccountId = o.MemberAccountId,
                FullName = "Іван Коваленко", Email = "ivan.kovalenko@example.com", BirthDate = new DateOnly(1985, 4, 12),
                Documents =
                [
                    Doc(IdCard, "004512345", "ID-картка Івана", 2000, 1650, [Personal], [60]),
                    Doc(ForeignPassport, "FA123456", "Закордонний паспорт Івана", 3500, 20, [Personal, Travel, Urgent], [90, 30]),
                    Doc(DriverLicense, "AXA102938", "Посвідчення водія кат. B", 4000, 2500, [Car], [30]),
                    Doc(CarInsurance, "EP-208877665", "ОСЦПВ на Skoda Octavia", 340, 25, [Car, Finance, Urgent], [14, 7]),
                    Doc(MedicalInsurance, "MED-55120", "Медичне страхування (робоче)", 300, 65, [Work], [14])
                ]
            },
            new()
            {
                AccountId = o.MemberAccountId,
                FullName = "Марко Коваленко", Email = "marko.kovalenko@example.com", BirthDate = new DateOnly(2012, 1, 20),
                Documents =
                [
                    Doc(IdCard, "004533311", "ID-картка Марка", 900, 2750, [Personal], [60]),
                    Doc(ForeignPassport, "FC111222", "Закордонний паспорт Марка", 1700, 120, [Personal, Travel], [90, 30]),
                    Doc(MedicalInsurance, "TRV-90012", "Туристичне страхування", 40, 5, [Travel, Urgent], [14, 3])
                ]
            },
            // ── Акаунт petro (member) ──
            new()
            {
                AccountId = o.SecondMemberAccountId,
                FullName = "Андрій Мельник", Email = "andrii.melnyk@example.com", BirthDate = new DateOnly(1990, 6, 15),
                Documents =
                [
                    Doc(ForeignPassport, "FD909090", "Закордонний паспорт Андрія", 2600, 1050, [Personal, Travel], [90]),
                    Doc(Visa, "US-B1B2-4410", "Віза США B1/B2", 1000, 800, [Travel, Work], [30]),
                    Doc(Certificate, "CERT-AWS-7781", "Сертифікат AWS Solutions Architect", 700, 45, [Work, Urgent], [30, 14]),
                    Doc(Contract, "DOG-2023-077", "Трудовий договір", 900, null, [Work, Finance], [])
                ]
            },
            new()
            {
                AccountId = o.SecondMemberAccountId,
                FullName = "Софія Шевчук", Email = "sofiia.shevchuk@example.com", BirthDate = new DateOnly(1979, 11, 30),
                Documents =
                [
                    Doc(IdCard, "004577788", "ID-картка Софії", 3500, 150, [Personal], [60]),
                    Doc(DriverLicense, "BXE556677", "Посвідчення водія кат. B", 1800, 1850, [Car], [30]),
                    Doc(CarInsurance, "EP-209911223", "ОСЦПВ на Toyota Yaris", 380, -10, [Car], [14]),
                    Doc(CarInsurance, "EP-199911000", "ОСЦПВ на Toyota Yaris (попередній поліс)", 760, -375, [Car], [14],
                        DocumentStatus.Replaced),
                    Doc(Contract, "DOV-NOT-55", "Нотаріальна довіреність", 100, null, [Finance], [])
                ]
            },
            // ── Акаунт viewer (лише читання) ──
            new()
            {
                AccountId = o.ViewerAccountId,
                FullName = "Наталія Бондар", Email = "nataliia.bondar@example.com", BirthDate = null,
                Documents =
                [
                    Doc(IdCard, "004522233", "ID-картка Наталії", 1200, 2450, [Personal], [60]),
                    Doc(MedicalInsurance, "MED-77001", "Медичне страхування (сімейне)", 330, 35, [Finance], [14, 30]),
                    Doc(Certificate, "CERT-FIRST-AID", "Сертифікат першої допомоги", 800, -60, [Work], [],
                        DocumentStatus.Cancelled),
                    Doc(Contract, "DOG-2025-301", "Договір страхування житла", 200, 165, [Finance], [30])
                ]
            }
        };

        // ── Великий набір для навантажувального тестування (акаунт petro) ──
        if (o.LoadTestDocuments > 0)
        {
            var loadOwner = new Owner
            {
                AccountId = o.SecondMemberAccountId,
                FullName = "Навантажувальний Тест", Email = "loadtest@example.com"
            };
            int[] types = [IdCard, ForeignPassport, Visa, DriverLicense, MedicalInsurance, CarInsurance, Certificate, Contract];
            for (var i = 1; i <= o.LoadTestDocuments; i++)
            {
                var typeId = types[i % types.Length];
                // Від −90 до +1409 днів: є прострочені, близькі й далекі. Видано ≥100 днів тому,
                // тож CHECK (ExpiryDate >= IssueDate) не порушується.
                int? expires = i % 9 == 0 ? null : (i * 37 % 1500) - 90;
                loadOwner.Documents.Add(Doc(typeId, $"LT-{i:D6}", $"Тестовий документ №{i}", 100 + i % 900, expires,
                    [1 + i % 6, 1 + (i + 3) % 6], [30]));
            }
            owners.Add(loadOwner);
        }

        return owners;
    }
}
