using DocOrganizer.DataLayer.Domain;
using Microsoft.EntityFrameworkCore;
using static DocOrganizer.DataLayer.Domain.DocumentTypeIds;
using static DocOrganizer.DataLayer.Domain.TagIds;

namespace DocOrganizer.DataLayer.Data;

/// <summary>
/// Тестові дані для UseSeeding/UseAsyncSeeding.
/// Довідники (DocumentTypes, Tags) заповнює HasData, тут — головні та залежні таблиці:
/// 6 власників, 26 документів, 31 нагадування, 37 позначок DocumentTags.
/// Дати рахуються відносно сьогодні, тому завжди є прострочені, «скоро спливають» і чинні документи.
/// </summary>
public static class SeedData
{
    public static void Seed(DbContext context)
    {
        // Ідемпотентність: якщо дані вже є — нічого не робимо.
        if (context.Set<Owner>().Any())
            return;

        context.AddRange(BuildOwners(DateOnly.FromDateTime(DateTime.Today)));
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken ct)
    {
        if (await context.Set<Owner>().AnyAsync(ct))
            return;

        context.AddRange(BuildOwners(DateOnly.FromDateTime(DateTime.Today)));
        await context.SaveChangesAsync(ct);
    }

    private static List<Owner> BuildOwners(DateOnly today)
    {
        var channelIndex = 0;

        // Локальна фабрика документа разом із графом: теги + нагадування.
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

        return
        [
            new Owner
            {
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
            new Owner
            {
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
            new Owner
            {
                FullName = "Марко Коваленко", Email = "marko.kovalenko@example.com", BirthDate = new DateOnly(2012, 1, 20),
                Documents =
                [
                    Doc(IdCard, "004533311", "ID-картка Марка", 900, 2750, [Personal], [60]),
                    Doc(ForeignPassport, "FC111222", "Закордонний паспорт Марка", 1700, 120, [Personal, Travel], [90, 30]),
                    Doc(MedicalInsurance, "TRV-90012", "Туристичне страхування", 40, 5, [Travel, Urgent], [14, 3])
                ]
            },
            new Owner
            {
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
            new Owner
            {
                FullName = "Андрій Мельник", Email = "andrii.melnyk@example.com", BirthDate = new DateOnly(1990, 6, 15),
                Documents =
                [
                    Doc(ForeignPassport, "FD909090", "Закордонний паспорт Андрія", 2600, 1050, [Personal, Travel], [90]),
                    Doc(Visa, "US-B1B2-4410", "Віза США B1/B2", 1000, 800, [Travel, Work], [30]),
                    Doc(Certificate, "CERT-AWS-7781", "Сертифікат AWS Solutions Architect", 700, 45, [Work, Urgent], [30, 14]),
                    Doc(Contract, "DOG-2023-077", "Трудовий договір", 900, null, [Work, Finance], [])
                ]
            },
            new Owner
            {
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
        ];
    }
}
