using DocOrganizer.DataLayer.Data;
using DocOrganizer.DataLayer.DataAccess;
using DocOrganizer.DataLayer.DataAccess.Ado;
using DocOrganizer.DataLayer.DataAccess.Ef;
using DocOrganizer.DataLayer.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.DataLayer.Demo;

/// <summary>Інтерактивне меню: кожен пункт — окремий сценарій роботи з даними (ADO.NET, EF Core, транзакції, конкурентність).</summary>
public sealed class DemoRunner(
    IDbContextFactory<DocumentsDbContext> factory,
    AdoDocumentRepository ado,
    EfDocumentService ef,
    EfDocumentQueries queries)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PrintMenu();
            var choice = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (choice is null or "0" or "Q")
                return;

            try
            {
                switch (choice)
                {
                    case "1": await ShowInitialStateAsync(ct); break;
                    case "2": await AdoCrudAsync(ct); break;
                    case "3": await AdoComplexQueriesAsync(ct); break;
                    case "4": await AdoTransactionAsync(ct); break;
                    case "5": await EfCrudAsync(ct); break;
                    case "6": await EfTransactionAsync(ct); break;
                    case "7": await LinqQueriesAsync(ct); break;
                    case "8": await PaginationAsync(ct); break;
                    case "9": await ConcurrencyAsync(ct); break;
                    case "10": await SqlDiagnosticsAsync(ct); break;
                    case "R": await RecreateDatabaseAsync(ct); break;
                    default: Ui.Warn("Невідомий пункт меню."); break;
                }
            }
            catch (OperationCanceledException)
            {
                Ui.Warn("Операцію скасовано (CancellationToken).");
                return;
            }
            catch (DataAccessException ex)
            {
                Ui.Error(ex.Message);
            }
            catch (SqlException ex)
            {
                // Текст SQL-помилки користувачу не показуємо — лише номер.
                Ui.Error($"Помилка SQL Server №{ex.Number}. Перевірте підключення до БД і чи застосовані міграції.");
            }
            catch (DbUpdateException ex)
            {
                Ui.Error($"Помилка збереження EF Core: {ex.GetType().Name}");
            }
            finally
            {
                SqlLog.Enabled = false;
            }

            Ui.Pause();
        }
    }

    private static void PrintMenu()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("DocOrganizer — організатор документів і строків дії");
        Console.ResetColor();
        Console.WriteLine("""
             1  Початковий стан даних (міграції, кількість рядків, власники, документи)
             2  ADO.NET: CRUD Owners + Documents (+ негативні сценарії)
             3  ADO.NET: складні запити (JOIN 3+ таблиць; GROUP BY + агрегати)
             4  ADO.NET: транзакція — Commit і Rollback
             5  EF Core: CRUD Owners, Documents, Reminders (+ негативні сценарії)
             6  EF Core: транзакційна операція над графом сутностей — Commit і Rollback
             7  EF Core: LINQ-запити (фільтр, сортування, DTO, Include, GroupBy, календар)
             8  EF Core: пагінація
             9  EF Core: optimistic concurrency (два DbContext, rowversion)
            10  SQL від EF Core: ToQueryString, логування, перевірка N+1
             R  Перестворити БД (EnsureDeleted + Migrate + Seed)
             0  Вихід
            """);
        Console.Write("Ваш вибір: ");
    }

    // =====================================================================
    // 1. Початковий стан
    // =====================================================================

    private async Task ShowInitialStateAsync(CancellationToken ct)
    {
        Ui.Header("1. Початковий стан даних");

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            Ui.Step("Застосовані міграції (таблиця __EFMigrationsHistory):");
            foreach (var migration in await db.Database.GetAppliedMigrationsAsync(ct))
                Ui.Info(migration);
        }

        Ui.Step("Кількість рядків у таблицях (ADO.NET, один SELECT з підзапитами):");
        foreach (var (table, rows) in await ado.GetTableCountsAsync(ct))
            Ui.Info($"{table,-15} {rows,5}");

        Ui.Step("Власники (EF Core: проєкція з o.Documents.Count() — один SQL-запит, без N+1):");
        foreach (var o in await queries.GetOwnerSummariesAsync(ct))
            Ui.Info($"#{o.Id,-3} {o.FullName,-18} {o.Email,-32} документів: {o.DocumentCount,2}, чинних: {o.ActiveCount,2}");

        Ui.Step("Усі документи (ADO.NET, SearchDocumentsAsync без фільтрів):");
        Ui.PrintDocuments(await ado.SearchDocumentsAsync(new DocumentSearchFilter(), ct), Today);
    }

    // =====================================================================
    // 2. ADO.NET CRUD
    // =====================================================================

    private async Task AdoCrudAsync(CancellationToken ct)
    {
        Ui.Header("2. ADO.NET — CRUD для Owners і Documents (Microsoft.Data.SqlClient)");
        var suffix = Random.Shared.Next(1000, 9999);

        Ui.Step("CREATE Owner: INSERT ... OUTPUT INSERTED.Id → ExecuteScalarAsync");
        var ownerId = await ado.AddOwnerAsync(
            new OwnerInput("Тест ADO Власник", $"ado.owner{suffix}@example.com", new DateOnly(1999, 1, 15)), ct);
        Ui.Ok($"Створено власника Id = {ownerId}");

        Ui.Step("READ Owner: SqlDataReader + IsDBNull для BirthDate");
        var owner = await ado.GetOwnerAsync(ownerId, ct)
                    ?? throw new DataAccessException("Щойно створений власник не знайдений.");
        Ui.Info(owner.ToString());

        Ui.Step("UPDATE Owner: ExecuteNonQueryAsync, аналіз кількості змінених рядків; BirthDate → NULL (DBNull.Value)");
        var updated = await ado.UpdateOwnerAsync(ownerId, new OwnerInput("Тест ADO Власник (оновлено)", owner.Email, null), ct);
        Ui.Ok($"Оновлено: {updated}. Тепер: {await ado.GetOwnerAsync(ownerId, ct)}");
        Ui.Info($"UPDATE неіснуючого Id = -1 → {await ado.UpdateOwnerAsync(-1, new OwnerInput("x", "x@x.x", null), ct)} (0 рядків)");

        Ui.Step("Негативний сценарій: дублікат email → SqlException 2627/2601 → DuplicateValueException");
        try
        {
            await ado.AddOwnerAsync(new OwnerInput("Дублікат", owner.Email, null), ct);
            Ui.Error("Очікувалась помилка унікальності!");
        }
        catch (DuplicateValueException ex)
        {
            Ui.Warn(ex.Message);
        }

        Ui.Step("CREATE Document для цього власника");
        var input = new DocumentInput(ownerId, DocumentTypeIds.Certificate, $"ADO-{suffix}", "Сертифікат курсу C#",
            Today.AddDays(-10), Today.AddYears(2), null, "Створено через ADO.NET");
        var documentId = await ado.AddDocumentAsync(input, ct);
        Ui.Ok($"Створено документ Id = {documentId}");

        Ui.Step("READ Document (JOIN Owners, DocumentTypes; ordinal-и за назвами стовпців)");
        Ui.PrintDocument(await ado.GetDocumentAsync(documentId, ct), Today);

        Ui.Step("UPDATE Document: змінюємо назву й строк дії");
        await ado.UpdateDocumentAsync(documentId,
            input with { Title = "Сертифікат курсу C# (продовжений)", ExpiryDate = Today.AddYears(3) }, ct);
        Ui.PrintDocument(await ado.GetDocumentAsync(documentId, ct), Today);

        Ui.Step("Негативний сценарій: ExpiryDate < IssueDate → CHECK CK_Documents_ExpiryAfterIssue (SqlException 547)");
        try
        {
            await ado.UpdateDocumentAsync(documentId, input with { ExpiryDate = input.IssueDate.AddDays(-1) }, ct);
        }
        catch (DataAccessException ex)
        {
            Ui.Warn(ex.Message);
        }

        Ui.Step("Негативний сценарій: документ для неіснуючого власника → FOREIGN KEY (SqlException 547)");
        try
        {
            await ado.AddDocumentAsync(input with { OwnerId = int.MaxValue, Number = $"ADO-FK-{suffix}" }, ct);
        }
        catch (DataAccessException ex)
        {
            Ui.Warn(ex.Message);
        }

        Ui.Step("Негативний сценарій: видалити власника, у якого є документ (FK Restrict)");
        Ui.Warn($"DeleteOwnerAsync → {await ado.DeleteOwnerAsync(ownerId, ct)}");

        Ui.Step("Список документів власника (GetDocumentsByOwnerAsync)");
        foreach (var d in await ado.GetDocumentsByOwnerAsync(ownerId, ct))
            Ui.PrintDocument(d, Today);

        Ui.Step("DELETE Document (Reminders і DocumentTags видалить ON DELETE CASCADE)");
        Ui.Ok($"Видалено: {await ado.DeleteDocumentAsync(documentId, ct)}");

        Ui.Step("DELETE Owner — тепер залежностей немає");
        Ui.Ok($"DeleteOwnerAsync → {await ado.DeleteOwnerAsync(ownerId, ct)}");
        Ui.Info($"Повторне читання: {(await ado.GetOwnerAsync(ownerId, ct))?.ToString() ?? "null — запис видалено"}");
    }

    // =====================================================================
    // 3. ADO.NET складні запити
    // =====================================================================

    private async Task AdoComplexQueriesAsync(CancellationToken ct)
    {
        Ui.Header("3. ADO.NET — складні запити");

        Ui.Step("Запит 1. JOIN Documents + Owners + DocumentTypes, фільтри: Status = Active, спливають ≤ 120 дн., без прострочених; ORDER BY ExpiryDate");
        Ui.PrintDocuments(await ado.SearchDocumentsAsync(
            new DocumentSearchFilter(Status: DocumentStatus.Active, ExpiringWithinDays: 120, IncludeExpired: false), ct), Today);

        Ui.Step("Запит 1 з іншим набором фільтрів: тег «Подорожі» (EXISTS по DocumentTags — 4-та таблиця)");
        Ui.PrintDocuments(await ado.SearchDocumentsAsync(new DocumentSearchFilter(TagId: TagIds.Travel), ct), Today);

        Ui.Step("Запит 1: пошук LIKE @search = «паспорт» + тип «Закордонний паспорт»");
        Ui.PrintDocuments(await ado.SearchDocumentsAsync(
            new DocumentSearchFilter(DocumentTypeId: DocumentTypeIds.ForeignPassport, Search: "паспорт"), ct), Today);

        Ui.Step("Запит 1: власний фільтр (Enter — пропустити параметр)");
        var ownerText = Ui.Ask("OwnerId: ");
        var typeText = Ui.Ask("DocumentTypeId (1–8): ");
        var filter = new DocumentSearchFilter(
            OwnerId: int.TryParse(ownerText, out var o) ? o : null,
            DocumentTypeId: int.TryParse(typeText, out var t) ? t : null);
        Ui.PrintDocuments(await ado.SearchDocumentsAsync(filter, ct), Today);

        Ui.Step("Запит 2. GROUP BY тип: COUNT, SUM(CASE…), MIN; LEFT JOIN — враховано й типи без документів; горизонт 60 дн.");
        PrintTypeStats(await ado.GetExpiryStatsByTypeAsync(60, 0, ct));

        Ui.Step("Запит 2 з HAVING COUNT(d.Id) >= 3 — лише типи з ≥ 3 чинними документами");
        PrintTypeStats(await ado.GetExpiryStatsByTypeAsync(60, 3, ct));
    }

    private static void PrintTypeStats(IReadOnlyList<TypeExpiryStatsDto> stats)
    {
        Ui.Info($"{"Тип",-22} | {"Чинних",6} | {"Простроч.",9} | {"≤60 дн.",7} | Найближче завершення");
        Ui.Info(new string('-', 80));
        foreach (var s in stats)
            Ui.Info($"{s.TypeName,-22} | {s.ActiveCount,6} | {s.ExpiredCount,9} | {s.ExpiringSoonCount,7} | {Ui.Fmt(s.NearestExpiry)}");
    }

    // =====================================================================
    // 4. ADO.NET транзакція
    // =====================================================================

    private async Task AdoTransactionAsync(CancellationToken ct)
    {
        Ui.Header("4. ADO.NET — транзакція «Продовження документа» (Documents + Reminders + DocumentTags)");

        var candidates = await GetRenewCandidatesAsync(ct);
        if (candidates.Count < 2)
        {
            Ui.Warn("Потрібно щонайменше 2 чинні закордонні паспорти. Перестворіть БД (пункт R).");
            return;
        }

        // ---------- COMMIT ----------
        var target = candidates[0];
        Ui.Step($"Сценарій COMMIT: продовжуємо #{target.Id} «{target.Title}» № {target.Number}");
        Ui.Info("До операції:");
        await PrintRenewStateAsync(target.Id, ct);

        var newExpiry = NewExpiryFor(target);
        var result = await ado.RenewDocumentAsync(
            new RenewDocumentRequest(target.Id, NewPassportNumber(), Today, newExpiry), ct);

        Ui.Ok($"COMMIT. Новий документ #{result.NewDocumentId}; видалено невідправлених нагадувань: {result.UnsentRemindersRemoved}; " +
              $"скопійовано тегів: {result.TagsCopied}; створено нагадувань: {result.RemindersCreated}");
        Ui.Info("Старий документ:");
        await PrintRenewStateAsync(target.Id, ct);
        Ui.Info("Новий документ:");
        await PrintRenewStateAsync(result.NewDocumentId, ct);

        // ---------- ROLLBACK 1: помилка SQL (UNIQUE) посеред транзакції ----------
        var victim = candidates[1];
        Ui.Step($"Сценарій ROLLBACK №1: продовжуємо #{victim.Id} з номером «{target.Number}», який уже має інший паспорт");
        Ui.Info("До операції:");
        await PrintRenewStateAsync(victim.Id, ct);
        try
        {
            await ado.RenewDocumentAsync(new RenewDocumentRequest(victim.Id, target.Number, Today, NewExpiryFor(victim)), ct);
            Ui.Error("Очікувалась помилка!");
        }
        catch (DuplicateValueException ex)
        {
            Ui.Warn(ex.Message);
        }
        Ui.Info("Після Rollback: статус і нагадування не змінилися, хоча UPDATE і DELETE уже були виконані:");
        await PrintRenewStateAsync(victim.Id, ct);

        // ---------- ROLLBACK 2: порушення бізнес-правила після UPDATE ----------
        var earlier = (victim.ExpiryDate ?? Today).AddDays(-1);
        Ui.Step($"Сценарій ROLLBACK №2: новий строк ({Ui.Fmt(earlier)}) раніше за поточний ({Ui.Fmt(victim.ExpiryDate)})");
        try
        {
            await ado.RenewDocumentAsync(
                new RenewDocumentRequest(victim.Id, NewPassportNumber(), earlier.AddYears(-1), earlier), ct);
            Ui.Error("Очікувалась помилка!");
        }
        catch (BusinessRuleException ex)
        {
            Ui.Warn(ex.Message);
        }
        Ui.Info("Після Rollback:");
        await PrintRenewStateAsync(victim.Id, ct);
    }

    // =====================================================================
    // 5. EF Core CRUD
    // =====================================================================

    private async Task EfCrudAsync(CancellationToken ct)
    {
        Ui.Header("5. EF Core — CRUD для Owners, Documents, Reminders");
        var suffix = Random.Shared.Next(1000, 9999);

        // ---------- Owners ----------
        Ui.Step("CREATE Owner: Add + SaveChangesAsync (Id заповнюється автоматично)");
        var ownerId = await ef.CreateOwnerAsync(
            new OwnerInput("Тест EF Власник", $"ef.owner{suffix}@example.com", new DateOnly(2001, 5, 5)), ct);
        Ui.Ok($"Створено власника Id = {ownerId}");

        Ui.Step("READ Owner (DTO-проєкція)");
        var owner = await ef.GetOwnerAsync(ownerId, ct)
                    ?? throw new DataAccessException("Щойно створений власник не знайдений.");
        Ui.Info(owner.ToString());

        Ui.Step("UPDATE Owner (tracking: UPDATE тільки змінених стовпців)");
        await ef.UpdateOwnerAsync(ownerId, new OwnerInput("Тест EF Власник (оновлено)", owner.Email, owner.BirthDate), ct);
        Ui.Ok((await ef.GetOwnerAsync(ownerId, ct))!.ToString());

        Ui.Step("Негативний сценарій: дублікат email → DbUpdateException(SqlException 2627/2601)");
        try
        {
            await ef.CreateOwnerAsync(new OwnerInput("Дублікат", owner.Email, null), ct);
            Ui.Error("Очікувалась помилка унікальності!");
        }
        catch (DuplicateValueException ex)
        {
            Ui.Warn(ex.Message);
        }

        // ---------- Documents ----------
        Ui.Step("CREATE Document як граф: Document + 2 DocumentTags + 2 Reminders → один SaveChanges");
        var input = new DocumentInput(ownerId, DocumentTypeIds.MedicalInsurance, $"EF-{suffix}", "Медичне страхування (тест EF)",
            Today, Today.AddMonths(6), $"files/EF-{suffix}.pdf", null);
        var documentId = await ef.CreateDocumentAsync(input, [TagIds.Finance, TagIds.Urgent], [30, 7], ct);
        Ui.Ok($"Створено документ Id = {documentId}");

        Ui.Step("READ Document: AsNoTracking + Include/ThenInclude + AsSplitQuery");
        var document = await ef.GetDocumentAsync(documentId, ct)
                       ?? throw new DataAccessException("Щойно створений документ не знайдений.");
        PrintDocumentEntity(document);

        Ui.Step("UPDATE Document з передачею Version (як у формі/Web API)");
        await ef.UpdateDocumentAsync(documentId, input with { Notes = "Оновлено через EF Core" }, document.Version, ct);
        PrintDocumentEntity((await ef.GetDocumentAsync(documentId, ct))!);

        Ui.Step("Негативний сценарій: повторне UPDATE зі СТАРИМ значенням Version → конфлікт");
        try
        {
            await ef.UpdateDocumentAsync(documentId, input with { Notes = "Спроба з застарілою версією" }, document.Version, ct);
            Ui.Error("Очікувався конфлікт!");
        }
        catch (ConcurrencyConflictException ex)
        {
            Ui.Warn(ex.Message);
        }

        // ---------- Reminders ----------
        Ui.Step("CREATE Reminder: за 14 днів, канал Push");
        var reminderId = await ef.AddReminderAsync(documentId, 14, ReminderChannel.Push, ct);
        Ui.Ok($"Створено нагадування Id = {reminderId}");

        Ui.Step("Негативні сценарії: дубль (DocumentId, DaysBefore) і CHECK DaysBefore ≤ 365");
        foreach (var days in new[] { 30, 500 })
        {
            try
            {
                await ef.AddReminderAsync(documentId, days, ReminderChannel.Email, ct);
                Ui.Error("Очікувалась помилка!");
            }
            catch (DataAccessException ex)
            {
                Ui.Warn(ex.Message);
            }
        }

        Ui.Step("UPDATE Reminder: позначити надісланим");
        Ui.Ok($"MarkReminderSentAsync → {await ef.MarkReminderSentAsync(reminderId, ct)}");
        PrintReminders(await ef.GetRemindersAsync(documentId, ct));

        Ui.Step("DELETE Reminder: ExecuteDeleteAsync (одна команда DELETE без завантаження сутності)");
        Ui.Ok($"DeleteReminderAsync → {await ef.DeleteReminderAsync(reminderId, ct)}");
        PrintReminders(await ef.GetRemindersAsync(documentId, ct));

        // ---------- Delete ----------
        Ui.Step("Негативний сценарій: DELETE Owner з документами → DeleteBehavior.Restrict (SqlException 547)");
        Ui.Warn($"DeleteOwnerAsync → {await ef.DeleteOwnerAsync(ownerId, ct)}");

        Ui.Step("DELETE Document → каскадно видаляються його Reminders і DocumentTags");
        Ui.Ok($"DeleteDocumentAsync → {await ef.DeleteDocumentAsync(documentId, ct)}; " +
              $"нагадувань цього документа в БД: {await ado.CountRemindersAsync(documentId, false, ct)}");

        Ui.Step("DELETE Owner — тепер дозволено");
        Ui.Ok($"DeleteOwnerAsync → {await ef.DeleteOwnerAsync(ownerId, ct)}");

        Ui.Step("Масова операція ExecuteUpdateAsync: позначити надісланими всі нагадування, дата яких настала");
        Ui.Ok($"Оновлено рядків: {await ef.MarkDueRemindersAsSentAsync(ct)}");
    }

    // =====================================================================
    // 6. EF Core транзакційна операція
    // =====================================================================

    private async Task EfTransactionAsync(CancellationToken ct)
    {
        Ui.Header("6. EF Core — «Продовження документа»: ExecuteUpdate + ExecuteDelete + граф у явній транзакції");

        var candidates = await GetRenewCandidatesAsync(ct);
        if (candidates.Count < 2)
        {
            Ui.Warn("Потрібно щонайменше 2 чинні закордонні паспорти. Перестворіть БД (пункт R).");
            return;
        }

        // ---------- COMMIT ----------
        var target = candidates[0];
        Ui.Step($"Сценарій COMMIT: продовжуємо #{target.Id} «{target.Title}» № {target.Number}");
        await PrintRenewStateAsync(target.Id, ct);

        var result = await ef.RenewDocumentAsync(
            new RenewDocumentRequest(target.Id, NewPassportNumber(), Today, NewExpiryFor(target)), ct);

        Ui.Ok($"COMMIT. Новий документ #{result.NewDocumentId}; видалено невідправлених нагадувань: {result.UnsentRemindersRemoved}; " +
              $"скопійовано тегів: {result.TagsCopied}; створено нагадувань: {result.RemindersCreated}");
        Ui.Info("Старий документ:");
        await PrintRenewStateAsync(target.Id, ct);
        Ui.Info("Новий документ (граф: теги + нагадування):");
        PrintDocumentEntity((await ef.GetDocumentAsync(result.NewDocumentId, ct))!);

        // ---------- ROLLBACK 1 ----------
        var victim = candidates[1];
        Ui.Step($"Сценарій ROLLBACK №1: #{victim.Id} з номером «{target.Number}» → помилка UNIQUE на SaveChanges (крок 3)");
        Ui.Info("До операції:");
        await PrintRenewStateAsync(victim.Id, ct);
        try
        {
            await ef.RenewDocumentAsync(new RenewDocumentRequest(victim.Id, target.Number, Today, NewExpiryFor(victim)), ct);
            Ui.Error("Очікувалась помилка!");
        }
        catch (DuplicateValueException ex)
        {
            Ui.Warn(ex.Message);
        }
        Ui.Info("Після Rollback (ExecuteUpdate і ExecuteDelete з кроків 1–2 скасовано):");
        await PrintRenewStateAsync(victim.Id, ct);

        // ---------- ROLLBACK 2 ----------
        Ui.Step($"Сценарій ROLLBACK №2: повторно продовжуємо вже замінений #{target.Id} → ExecuteUpdate змінив 0 рядків");
        try
        {
            await ef.RenewDocumentAsync(
                new RenewDocumentRequest(target.Id, NewPassportNumber(), Today, Today.AddYears(20)), ct);
            Ui.Error("Очікувалась помилка!");
        }
        catch (BusinessRuleException ex)
        {
            Ui.Warn(ex.Message);
        }
    }

    // =====================================================================
    // 7. LINQ-запити
    // =====================================================================

    private async Task LinqQueriesAsync(CancellationToken ct)
    {
        Ui.Header("7. EF Core — LINQ-запити");

        Ui.Step("1) Фільтрація за кількома параметрами (Status = Active, тег «Подорожі», без прострочених) — сутності, AsNoTracking");
        var filtered = await queries.FilterDocumentsAsync(
            new DocumentSearchFilter(Status: DocumentStatus.Active, TagId: TagIds.Travel, IncludeExpired: false), ct);
        Ui.PrintDocuments(filtered.Select(d => new DocumentListItemDto(
            d.Id, d.Title, d.Number, d.Owner.FullName, d.DocumentType.Name, d.ExpiryDate, d.Status)).ToList(), Today);

        Ui.Step("2) Сортування: за власником ↑, далі за датою завершення, ThenBy(Id) (перші 10)");
        Ui.PrintDocuments(await queries.GetSortedDocumentsAsync(DocumentSort.Owner, false, 10, ct), Today);

        Ui.Step("3) DTO-проєкція з навігаціями (Owner.FullName, DocumentType.Name): що спливає протягом 60 днів");
        Ui.PrintDocuments(await queries.GetExpiringSoonAsync(60, ct), Today);

        var firstOwner = (await queries.GetOwnerSummariesAsync(ct)).FirstOrDefault();
        if (firstOwner is not null)
        {
            Ui.Step($"4) Пов'язані дані: Include(Documents).ThenInclude(DocumentType / DocumentTags.Tag) для «{firstOwner.FullName}»");
            var owner = await queries.GetOwnerWithDocumentsAsync(firstOwner.Id, ct);
            if (owner is not null)
            {
                Ui.Info($"{owner.FullName} <{owner.Email}>");
                foreach (var d in owner.Documents.OrderBy(x => x.ExpiryDate))
                {
                    var tags = string.Join(", ", d.DocumentTags.Select(x => x.Tag.Name));
                    Ui.Info($"   - {d.DocumentType.Name}: {d.Title} № {d.Number}, до {Ui.Fmt(d.ExpiryDate)} [{tags}]");
                }
            }
        }

        Ui.Step("5) GroupBy з агрегатами: Count, Count(умова), Min — статистика чинних документів за власниками");
        Ui.Info($"{"Власник",-20} | {"Чинних",6} | {"Простроч.",9} | {"≤30 дн.",7} | Найраніше завершення");
        Ui.Info(new string('-', 75));
        foreach (var s in await queries.GetOwnerStatsAsync(30, ct))
            Ui.Info($"{s.OwnerName,-20} | {s.ActiveCount,6} | {s.ExpiredCount,9} | {s.ExpiringSoonCount,7} | {Ui.Fmt(s.EarliestExpiry)}");

        Ui.Step("5б) Календар завершення строків: GroupBy (рік, місяць) на 24 місяці вперед");
        foreach (var item in await queries.GetExpiryCalendarAsync(24, ct))
            Ui.Info($"{item.Month:00}.{item.Year}  {new string('#', item.Count)} {item.Count}");

        Ui.Info("\n6) Пагінація — окремий пункт меню 8.");
    }

    // =====================================================================
    // 8. Пагінація
    // =====================================================================

    private async Task PaginationAsync(CancellationToken ct)
    {
        Ui.Header("8. EF Core — пагінація (Skip/Take + стабільне сортування)");

        var page = 1;
        const int pageSize = 5;
        var sort = DocumentSort.ExpiryDate;
        var descending = false;

        while (true)
        {
            var result = await queries.GetDocumentsPageAsync(page, pageSize, sort, descending, ct);
            Ui.Step($"Сторінка {result.PageNumber} з {result.TotalPages} (усього {result.TotalCount}; сортування: {sort}{(descending ? " ↓" : " ↑")})");
            Ui.PrintDocuments(result.Items.ToList(), Today);

            var cmd = Ui.Ask("[n] наступна  [p] попередня  [s] змінити сортування  [d] напрям  [q] вихід: ").ToLowerInvariant();
            switch (cmd)
            {
                case "n" when page < result.TotalPages: page++; break;
                case "p" when page > 1: page--; break;
                case "s": sort = (DocumentSort)(((int)sort + 1) % 3); page = 1; break;
                case "d": descending = !descending; page = 1; break;
                case "q": return;
                default: Ui.Warn("Немає такої сторінки або команди."); break;
            }
        }
    }

    // =====================================================================
    // 9. Optimistic concurrency
    // =====================================================================

    private async Task ConcurrencyAsync(CancellationToken ct)
    {
        Ui.Header("9. Optimistic concurrency: два DbContext змінюють один документ");

        int documentId;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            documentId = await db.Documents
                .Where(d => d.Status == DocumentStatus.Active)
                .OrderBy(d => d.Id)
                .Select(d => d.Id)
                .FirstAsync(ct);
        }

        await using var userA = await factory.CreateDbContextAsync(ct);
        await using var userB = await factory.CreateDbContextAsync(ct);

        Ui.Step($"Користувачі A і B читають документ #{documentId} (кожен у своєму DbContext)");
        var docA = await userA.Documents.SingleAsync(d => d.Id == documentId, ct);
        var docB = await userB.Documents.SingleAsync(d => d.Id == documentId, ct);
        Ui.Info($"Version у A: {Convert.ToHexString(docA.Version)}; у B: {Convert.ToHexString(docB.Version)}");

        Ui.Step("A змінює примітку і зберігає → rowversion у БД змінюється");
        docA.Notes = $"Змінив користувач A о {DateTime.Now:HH:mm:ss}";
        await userA.SaveChangesAsync(ct);
        Ui.Ok($"A зберіг. Нова Version: {Convert.ToHexString(docA.Version)}");

        Ui.Step("B (зі старою Version) теж змінює примітку і зберігає → UPDATE ... WHERE Id = @id AND Version = @old → 0 рядків");
        docB.Notes = $"Змінив користувач B о {DateTime.Now:HH:mm:ss}";
        try
        {
            await userB.SaveChangesAsync(ct);
            Ui.Error("Очікувався DbUpdateConcurrencyException!");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Ui.Warn("DbUpdateConcurrencyException: запис змінено іншим користувачем після читання.");

            var entry = ex.Entries.Single();
            var databaseValues = await entry.GetDatabaseValuesAsync(ct);
            if (databaseValues is null)
            {
                Ui.Error("Запис уже видалено іншим користувачем.");
                return;
            }

            Ui.Info($"У БД зараз:      «{databaseValues.GetValue<string?>(nameof(Document.Notes))}»");
            Ui.Info($"B хоче зберегти: «{entry.CurrentValues.GetValue<string?>(nameof(Document.Notes))}»");

            var answer = Ui.Ask("Перезаписати значенням B? [т] так — «перемагає клієнт» / [н] ні — «перемагає БД»: ");
            if (answer.StartsWith('т') || answer.StartsWith('y'))
            {
                // Беремо актуальну Version з БД як початкову — повторний UPDATE пройде.
                entry.OriginalValues.SetValues(databaseValues);
                await userB.SaveChangesAsync(ct);
                Ui.Ok("Збережено значення B (client wins).");
            }
            else
            {
                await entry.ReloadAsync(ct);
                Ui.Ok($"Зміни B відкинуто, завантажено значення з БД (database wins): «{docB.Notes}»");
            }
        }
    }

    // =====================================================================
    // 10. SQL від EF Core
    // =====================================================================

    private async Task SqlDiagnosticsAsync(CancellationToken ct)
    {
        Ui.Header("10. SQL, який формує EF Core");

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            Ui.Step("ToQueryString №1 — фільтрація (Include → INNER JOIN; DocumentTags.Any → EXISTS; Contains → LIKE)");
            PrintSql(EfDocumentQueries.BuildFilterQuery(db,
                new DocumentSearchFilter(Status: DocumentStatus.Active, TagId: TagIds.Travel, ExpiringWithinDays: 365, Search: "паспорт"),
                Today).ToQueryString());

            Ui.Step("ToQueryString №2 — GroupBy з агрегатами (GROUP BY, COUNT, COUNT(CASE…), MIN)");
            PrintSql(EfDocumentQueries.BuildOwnerStatsQuery(db, Today, 30).ToQueryString());

            Ui.Step("ToQueryString №3 — пагінація з DTO-проєкцією (OFFSET … FETCH NEXT …)");
            PrintSql(EfDocumentQueries.BuildPageQuery(db, DocumentSort.ExpiryDate, false, 2, 5).ToQueryString());
        }

        var firstOwner = (await queries.GetOwnerSummariesAsync(ct)).First();

        Ui.Step("EF Core logging (LogTo): Include кількох колекцій + AsSplitQuery → фіксована кількість SELECT");
        SqlLog.Reset();
        SqlLog.Enabled = true;
        await queries.GetOwnerWithDocumentsAsync(firstOwner.Id, ct);
        SqlLog.Enabled = false;
        Ui.Ok($"Виконано SQL-команд: {SqlLog.CommandCount} (не залежить від кількості документів)");

        Ui.Step("Перевірка N+1: «поганий» варіант — цикл з окремим запитом для кожного власника");
        SqlLog.Reset();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var owners = await db.Owners.AsNoTracking().ToListAsync(ct);
            foreach (var owner in owners)
                _ = await db.Documents.CountAsync(d => d.OwnerId == owner.Id, ct);   // N додаткових запитів
        }
        var naive = SqlLog.CommandCount;

        SqlLog.Reset();
        await queries.GetOwnerSummariesAsync(ct);   // проєкція з o.Documents.Count()
        var projected = SqlLog.CommandCount;

        Ui.Warn($"Цикл (N+1): {naive} SQL-команд");
        Ui.Ok($"Проєкція (GetOwnerSummariesAsync, використовується в демо): {projected} SQL-команда");
    }

    private static void PrintSql(string sql)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(sql);
        Console.ResetColor();
    }

    // =====================================================================
    // R. Перестворення БД
    // =====================================================================

    private async Task RecreateDatabaseAsync(CancellationToken ct)
    {
        Ui.Header("R. Перестворення бази даних");
        if (!Ui.Ask("Усі дані буде видалено. Продовжити? [т/н]: ").StartsWith('т'))
            return;

        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.MigrateAsync(ct);   // застосовує міграції + викликає UseAsyncSeeding
        Ui.Ok("БД створено заново, міграції застосовано, тестові дані додано.");
    }

    // =====================================================================
    // Допоміжне
    // =====================================================================

    private sealed record RenewCandidate(int Id, string Number, string Title, DateOnly? ExpiryDate);

    /// <summary>Чинні закордонні паспорти, від найближчого завершення.</summary>
    private async Task<List<RenewCandidate>> GetRenewCandidatesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Documents
            .Where(d => d.DocumentTypeId == DocumentTypeIds.ForeignPassport
                        && d.Status == DocumentStatus.Active
                        && d.ExpiryDate != null)
            .OrderBy(d => d.ExpiryDate)
            .ThenBy(d => d.Id)
            .Select(d => new RenewCandidate(d.Id, d.Number, d.Title, d.ExpiryDate))
            .ToListAsync(ct);
    }

    private async Task PrintRenewStateAsync(int documentId, CancellationToken ct)
    {
        var d = await ado.GetDocumentAsync(documentId, ct);
        var all = await ado.CountRemindersAsync(documentId, onlyUnsent: false, ct);
        var unsent = await ado.CountRemindersAsync(documentId, onlyUnsent: true, ct);
        Ui.Info($"   #{documentId}: статус = {d?.Status}, № {d?.Number}, діє до {Ui.Fmt(d?.ExpiryDate)}, " +
                $"нагадувань: {all} (невідправлених: {unsent}); {d?.Notes}");
    }

    private static DateOnly NewExpiryFor(RenewCandidate candidate)
    {
        var from = candidate.ExpiryDate is { } e && e > Today ? e : Today;
        return from.AddYears(10);
    }

    private static string NewPassportNumber() => $"FN{Random.Shared.Next(100000, 999999)}";

    private static void PrintDocumentEntity(Document d)
    {
        Ui.Info($"#{d.Id} «{d.Title}» — {d.DocumentType.Name} № {d.Number}, власник: {d.Owner.FullName}");
        Ui.Info($"   видано {Ui.Fmt(d.IssueDate)}, діє до {Ui.Fmt(d.ExpiryDate)}, статус {d.Status}, Version {Convert.ToHexString(d.Version)}");
        Ui.Info($"   теги: {string.Join(", ", d.DocumentTags.Select(t => t.Tag.Name))}");
        Ui.Info($"   нагадування: {string.Join("; ", d.Reminders.OrderBy(r => r.RemindOn).Select(r => $"{Ui.Fmt(r.RemindOn)} ({r.DaysBefore} дн., {r.Channel}{(r.IsSent ? ", надіслано" : "")})"))}");
        Ui.Info($"   примітка: {d.Notes ?? "—"}");
    }

    private static void PrintReminders(IReadOnlyList<ReminderDto> reminders)
    {
        foreach (var r in reminders)
            Ui.Info($"   нагадування #{r.Id}: {Ui.Fmt(r.RemindOn)} (за {r.DaysBefore} дн.), {r.Channel}, надіслано: {r.IsSent}");
    }
}
