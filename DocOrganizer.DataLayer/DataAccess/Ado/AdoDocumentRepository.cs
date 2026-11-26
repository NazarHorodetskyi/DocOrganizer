using System.Data;
using System.Text;
using DocOrganizer.DataLayer.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DocOrganizer.DataLayer.DataAccess.Ado;

/// <summary>
/// Прямий доступ до SQL Server через ADO.NET (Microsoft.Data.SqlClient).
/// Кожен метод: власне з'єднання (з пулу) → параметризована команда → await using → повернення DTO.
/// </summary>
public sealed class AdoDocumentRepository(string connectionString, ILogger<AdoDocumentRepository> logger)
{
    // =====================================================================
    // 4. CRUD для Owners
    // =====================================================================

    public async Task<int> AddOwnerAsync(OwnerInput input, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO Owners (FullName, Email, BirthDate)
            OUTPUT INSERTED.Id
            VALUES (@fullName, @email, @birthDate);
            """;

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        AddOwnerParameters(command, input);

        await connection.OpenAsync(ct);
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new DuplicateValueException($"Власник з email «{input.Email}» уже існує.", ex);
        }
    }

    public async Task<OwnerDto?> GetOwnerAsync(int id, CancellationToken ct)
    {
        const string sql = "SELECT Id, FullName, Email, BirthDate FROM Owners WHERE Id = @id;";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadOwner(reader) : null;
    }

    public async Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(CancellationToken ct)
    {
        const string sql = "SELECT Id, FullName, Email, BirthDate FROM Owners ORDER BY FullName, Id;";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var owners = new List<OwnerDto>();
        while (await reader.ReadAsync(ct))
            owners.Add(ReadOwner(reader));
        return owners;
    }

    /// <returns>false — власника з таким Id немає.</returns>
    public async Task<bool> UpdateOwnerAsync(int id, OwnerInput input, CancellationToken ct)
    {
        const string sql = """
            UPDATE Owners
            SET FullName = @fullName, Email = @email, BirthDate = @birthDate
            WHERE Id = @id;
            """;

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        AddOwnerParameters(command, input);

        await connection.OpenAsync(ct);
        try
        {
            var affected = await command.ExecuteNonQueryAsync(ct);
            return affected > 0;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new DuplicateValueException($"Email «{input.Email}» уже зайнятий іншим власником.", ex);
        }
    }

    public async Task<DeleteResult> DeleteOwnerAsync(int id, CancellationToken ct)
    {
        const string sql = "DELETE FROM Owners WHERE Id = @id;";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;

        await connection.OpenAsync(ct);
        try
        {
            var affected = await command.ExecuteNonQueryAsync(ct);
            return affected > 0 ? DeleteResult.Deleted : DeleteResult.NotFound;
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            // FK Documents.OwnerId налаштований як Restrict (NO ACTION).
            return DeleteResult.HasDependencies;
        }
    }

    // =====================================================================
    // 4. CRUD для Documents (пов'язана з Owners сутність)
    // =====================================================================

    private const string InsertDocumentSql = """
        INSERT INTO Documents (OwnerId, DocumentTypeId, Number, Title, IssueDate, ExpiryDate, FilePath, Notes, Status)
        OUTPUT INSERTED.Id
        VALUES (@ownerId, @documentTypeId, @number, @title, @issueDate, @expiryDate, @filePath, @notes, @status);
        """;

    private const string DocumentDetailsSelect = """
        SELECT d.Id, d.OwnerId, o.FullName AS OwnerName, d.DocumentTypeId, t.Name AS TypeName,
               d.Number, d.Title, d.IssueDate, d.ExpiryDate, d.Status, d.FilePath, d.Notes
        FROM Documents AS d
        INNER JOIN Owners AS o ON o.Id = d.OwnerId
        INNER JOIN DocumentTypes AS t ON t.Id = d.DocumentTypeId
        """;

    public async Task<int> AddDocumentAsync(DocumentInput input, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(InsertDocumentSql, connection);
        AddDocumentParameters(command, input);
        command.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = nameof(DocumentStatus.Active);

        await connection.OpenAsync(ct);
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new DuplicateValueException($"Документ цього типу з номером «{input.Number}» уже існує.", ex);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw new DataAccessException(
                "Порушено обмеження: неіснуючий власник/тип або дата завершення раніше дати видачі.", ex);
        }
    }

    public async Task<DocumentDetailsDto?> GetDocumentAsync(int id, CancellationToken ct)
    {
        var sql = DocumentDetailsSelect + "\nWHERE d.Id = @id;";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadDocumentDetails(reader, DocumentOrdinals.From(reader));
    }

    public async Task<IReadOnlyList<DocumentDetailsDto>> GetDocumentsByOwnerAsync(int ownerId, CancellationToken ct)
    {
        var sql = DocumentDetailsSelect + """

            WHERE d.OwnerId = @ownerId
            ORDER BY CASE WHEN d.ExpiryDate IS NULL THEN 1 ELSE 0 END, d.ExpiryDate, d.Id;
            """;

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ownerId", SqlDbType.Int).Value = ownerId;

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        // Ordinal-и отримуємо один раз за назвами стовпців, а не на кожному рядку.
        var ordinals = DocumentOrdinals.From(reader);
        var documents = new List<DocumentDetailsDto>();
        while (await reader.ReadAsync(ct))
            documents.Add(ReadDocumentDetails(reader, ordinals));
        return documents;
    }

    /// <returns>false — документа з таким Id немає.</returns>
    public async Task<bool> UpdateDocumentAsync(int id, DocumentInput input, CancellationToken ct)
    {
        const string sql = """
            UPDATE Documents
            SET OwnerId = @ownerId,
                DocumentTypeId = @documentTypeId,
                Number = @number,
                Title = @title,
                IssueDate = @issueDate,
                ExpiryDate = @expiryDate,
                FilePath = @filePath,
                Notes = @notes
            WHERE Id = @id;
            """;

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        AddDocumentParameters(command, input);

        await connection.OpenAsync(ct);
        try
        {
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new DuplicateValueException($"Документ цього типу з номером «{input.Number}» уже існує.", ex);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw new DataAccessException(
                "Порушено обмеження: неіснуючий власник/тип або дата завершення раніше дати видачі.", ex);
        }
    }

    /// <summary>Reminders і DocumentTags документа видаляє сам SQL Server (ON DELETE CASCADE).</summary>
    public async Task<bool> DeleteDocumentAsync(int id, CancellationToken ct)
    {
        const string sql = "DELETE FROM Documents WHERE Id = @id;";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;

        await connection.OpenAsync(ct);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    // =====================================================================
    // Допоміжні запити для демонстрації (ExecuteScalar / кілька значень)
    // =====================================================================

    public async Task<int> CountRemindersAsync(int documentId, bool onlyUnsent, CancellationToken ct)
    {
        var sql = "SELECT COUNT(*) FROM Reminders WHERE DocumentId = @documentId"
                  + (onlyUnsent ? " AND IsSent = 0;" : ";");

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@documentId", SqlDbType.Int).Value = documentId;

        await connection.OpenAsync(ct);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<(string Table, int Rows)>> GetTableCountsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT (SELECT COUNT(*) FROM Owners)        AS Owners,
                   (SELECT COUNT(*) FROM DocumentTypes) AS DocumentTypes,
                   (SELECT COUNT(*) FROM Tags)          AS Tags,
                   (SELECT COUNT(*) FROM Documents)     AS Documents,
                   (SELECT COUNT(*) FROM Reminders)     AS Reminders,
                   (SELECT COUNT(*) FROM DocumentTags)  AS DocumentTags;
            """;

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var result = new List<(string Table, int Rows)>();
        for (var i = 0; i < reader.FieldCount; i++)
            result.Add((reader.GetName(i), reader.GetInt32(i)));
        return result;
    }

    // =====================================================================
    // 5а. Складний запит: JOIN 3+ таблиць з необов'язковими фільтрами та сортуванням
    // =====================================================================

    public async Task<IReadOnlyList<DocumentListItemDto>> SearchDocumentsAsync(
        DocumentSearchFilter filter, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var sql = new StringBuilder("""
            SELECT d.Id, d.Title, d.Number, o.FullName AS OwnerName, t.Name AS TypeName, d.ExpiryDate, d.Status
            FROM Documents AS d
            INNER JOIN Owners AS o ON o.Id = d.OwnerId
            INNER JOIN DocumentTypes AS t ON t.Id = d.DocumentTypeId
            WHERE 1 = 1
            """);

        await using var connection = new SqlConnection(connectionString);
        await using var command = connection.CreateCommand();

        // Умови додаються динамічно, але ЗНАЧЕННЯ завжди йдуть параметрами.
        if (filter.OwnerId is not null)
        {
            sql.Append("\n  AND d.OwnerId = @ownerId");
            command.Parameters.Add("@ownerId", SqlDbType.Int).Value = filter.OwnerId.Value;
        }

        if (filter.DocumentTypeId is not null)
        {
            sql.Append("\n  AND d.DocumentTypeId = @typeId");
            command.Parameters.Add("@typeId", SqlDbType.Int).Value = filter.DocumentTypeId.Value;
        }

        if (filter.Status is not null)
        {
            sql.Append("\n  AND d.Status = @status");
            command.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = filter.Status.Value.ToString();
        }

        if (filter.TagId is not null)
        {
            // Четверта таблиця — через EXISTS, щоб не дублювати рядки документів.
            sql.Append("\n  AND EXISTS (SELECT 1 FROM DocumentTags AS dt WHERE dt.DocumentId = d.Id AND dt.TagId = @tagId)");
            command.Parameters.Add("@tagId", SqlDbType.Int).Value = filter.TagId.Value;
        }

        if (filter.ExpiringWithinDays is not null)
        {
            sql.Append("\n  AND d.ExpiryDate IS NOT NULL AND d.ExpiryDate <= @horizon");
            command.Parameters.Add("@horizon", SqlDbType.Date).Value =
                today.AddDays(filter.ExpiringWithinDays.Value).ToDateTime(TimeOnly.MinValue);
        }

        if (!filter.IncludeExpired)
        {
            sql.Append("\n  AND (d.ExpiryDate IS NULL OR d.ExpiryDate >= @today)");
            command.Parameters.Add("@today", SqlDbType.Date).Value = today.ToDateTime(TimeOnly.MinValue);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            sql.Append("\n  AND (d.Title LIKE @search OR d.Number LIKE @search)");
            command.Parameters.Add("@search", SqlDbType.NVarChar, 210).Value = $"%{filter.Search.Trim()}%";
        }

        // Безстрокові — в кінці; далі за датою завершення, власником; Id — для стабільності.
        sql.Append("\nORDER BY CASE WHEN d.ExpiryDate IS NULL THEN 1 ELSE 0 END, d.ExpiryDate, o.FullName, d.Id;");
        command.CommandText = sql.ToString();

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var result = new List<DocumentListItemDto>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new DocumentListItemDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ReadNullableDate(reader, 5),
                Enum.Parse<DocumentStatus>(reader.GetString(6))));
        }

        return result;
    }

    // =====================================================================
    // 5б. Агрегований запит: GROUP BY + COUNT, SUM, MIN + HAVING
    // =====================================================================

    public async Task<IReadOnlyList<TypeExpiryStatsDto>> GetExpiryStatsByTypeAsync(
        int horizonDays, int minActiveCount, CancellationToken ct)
    {
        // LEFT JOIN — щоб у результат потрапили й типи без чинних документів.
        // Умова Status = 'Active' стоїть в ON, а не в WHERE, інакше LEFT JOIN перетворився б на INNER.
        const string sql = """
            SELECT t.Id,
                   t.Name,
                   COUNT(d.Id) AS ActiveCount,
                   SUM(CASE WHEN d.ExpiryDate < @today THEN 1 ELSE 0 END) AS ExpiredCount,
                   SUM(CASE WHEN d.ExpiryDate >= @today AND d.ExpiryDate <= @horizon THEN 1 ELSE 0 END) AS ExpiringSoonCount,
                   MIN(CASE WHEN d.ExpiryDate >= @today THEN d.ExpiryDate END) AS NearestExpiry
            FROM DocumentTypes AS t
            LEFT JOIN Documents AS d
                   ON d.DocumentTypeId = t.Id AND d.Status = N'Active'
            GROUP BY t.Id, t.Name
            HAVING COUNT(d.Id) >= @minActiveCount
            ORDER BY ExpiringSoonCount DESC, ExpiredCount DESC, t.Name;
            """;

        var today = DateOnly.FromDateTime(DateTime.Today);

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@today", SqlDbType.Date).Value = today.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@horizon", SqlDbType.Date).Value = today.AddDays(horizonDays).ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@minActiveCount", SqlDbType.Int).Value = minActiveCount;

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var idOrdinal = reader.GetOrdinal("Id");
        var nameOrdinal = reader.GetOrdinal("Name");
        var activeOrdinal = reader.GetOrdinal("ActiveCount");
        var expiredOrdinal = reader.GetOrdinal("ExpiredCount");
        var soonOrdinal = reader.GetOrdinal("ExpiringSoonCount");
        var nearestOrdinal = reader.GetOrdinal("NearestExpiry");

        var stats = new List<TypeExpiryStatsDto>();
        while (await reader.ReadAsync(ct))
        {
            stats.Add(new TypeExpiryStatsDto(
                reader.GetInt32(idOrdinal),
                reader.GetString(nameOrdinal),
                reader.GetInt32(activeOrdinal),
                reader.GetInt32(expiredOrdinal),
                reader.GetInt32(soonOrdinal),
                ReadNullableDate(reader, nearestOrdinal)));  // MIN(...) = NULL, якщо немає майбутніх дат
        }

        return stats;
    }

    // =====================================================================
    // 6. Транзакція: продовження документа (Documents + Reminders + DocumentTags)
    // =====================================================================

    /// <summary>
    /// Продовжує документ: старий → Replaced, його невідправлені нагадування видаляються,
    /// створюється новий документ, копіюються теги, створюються нові нагадування.
    /// Будь-яка помилка або порушення правила → Rollback усіх змін.
    /// </summary>
    public async Task<RenewResult> RenewDocumentAsync(RenewDocumentRequest request, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Перевірки, що не потребують даних з БД, — до початку транзакції.
        if (request.NewExpiryDate < request.NewIssueDate)
            throw new BusinessRuleException("Дата завершення дії не може бути раніше дати видачі.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            // Крок 1. Атомарно позначаємо старий документ заміненим і отримуємо його дані (OUTPUT DELETED.*).
            var old = await MarkReplacedAsync(connection, transaction, request.DocumentId, ct)
                      ?? throw new BusinessRuleException(
                          $"Документ #{request.DocumentId} не знайдено або він уже не чинний.");

            // Крок 2. Бізнес-правило: новий строк має бути пізнішим за старий.
            // UPDATE з кроку 1 уже виконано — Rollback його скасує.
            if (old.ExpiryDate is { } oldExpiry && request.NewExpiryDate <= oldExpiry)
                throw new BusinessRuleException(
                    $"Новий строк дії ({request.NewExpiryDate:dd.MM.yyyy}) має бути пізнішим за поточний ({oldExpiry:dd.MM.yyyy}).");

            // Крок 3. Невідправлені нагадування старого документа більше не потрібні.
            var removed = await DeleteUnsentRemindersAsync(connection, transaction, request.DocumentId, ct);

            // Крок 4. Новий документ (UNIQUE (DocumentTypeId, Number) може кинути 2627).
            var newInput = new DocumentInput(
                old.OwnerId, old.DocumentTypeId, request.NewNumber, old.Title,
                request.NewIssueDate, request.NewExpiryDate,
                request.FilePath ?? old.FilePath,
                $"Продовження документа #{request.DocumentId}");
            var newId = await InsertDocumentAsync(connection, transaction, newInput, ct);

            // Крок 5. Копіюємо теги старого документа.
            var tagsCopied = await CopyTagsAsync(connection, transaction, request.DocumentId, newId, ct);

            // Крок 6. Нагадування для нового документа: типовий відступ типу + за 7 днів.
            var remindersCreated = await CreateRemindersAsync(
                connection, transaction, newId, old.DocumentTypeId, request.NewExpiryDate, today, ct);

            await transaction.CommitAsync(ct);
            return new RenewResult(newId, remindersCreated, tagsCopied, removed);
        }
        catch (Exception ex)
        {
            try
            {
                // CancellationToken.None: скасування не повинно завадити відкату.
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackError)
            {
                // З'єднання вже втрачене — незафіксовану транзакцію відкотить сервер.
                logger.LogWarning(rollbackError, "Rollback failed.");
            }

            if (ex is SqlException { Number: 2627 or 2601 } sqlEx)
                throw new DuplicateValueException(
                    $"Документ цього типу з номером «{request.NewNumber}» уже існує. Усі зміни скасовано (Rollback).",
                    sqlEx);

            throw;
        }
    }

    private sealed record OldDocumentInfo(int OwnerId, int DocumentTypeId, string Title, DateOnly? ExpiryDate, string? FilePath);

    private static async Task<OldDocumentInfo?> MarkReplacedAsync(
        SqlConnection connection, SqlTransaction transaction, int documentId, CancellationToken ct)
    {
        // Умова Status = 'Active' захищає від подвійного продовження одного документа.
        const string sql = """
            UPDATE Documents
            SET Status = @replaced
            OUTPUT DELETED.OwnerId, DELETED.DocumentTypeId, DELETED.Title, DELETED.ExpiryDate, DELETED.FilePath
            WHERE Id = @id AND Status = @active;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@id", SqlDbType.Int).Value = documentId;
        command.Parameters.Add("@replaced", SqlDbType.NVarChar, 20).Value = nameof(DocumentStatus.Replaced);
        command.Parameters.Add("@active", SqlDbType.NVarChar, 20).Value = nameof(DocumentStatus.Active);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new OldDocumentInfo(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            ReadNullableDate(reader, 3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static async Task<int> DeleteUnsentRemindersAsync(
        SqlConnection connection, SqlTransaction transaction, int documentId, CancellationToken ct)
    {
        const string sql = "DELETE FROM Reminders WHERE DocumentId = @documentId AND IsSent = 0;";

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@documentId", SqlDbType.Int).Value = documentId;
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> InsertDocumentAsync(
        SqlConnection connection, SqlTransaction transaction, DocumentInput input, CancellationToken ct)
    {
        await using var command = new SqlCommand(InsertDocumentSql, connection, transaction);
        AddDocumentParameters(command, input);
        command.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = nameof(DocumentStatus.Active);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<int> CopyTagsAsync(
        SqlConnection connection, SqlTransaction transaction, int fromDocumentId, int toDocumentId, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO DocumentTags (DocumentId, TagId)
            SELECT @toId, TagId
            FROM DocumentTags
            WHERE DocumentId = @fromId;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@toId", SqlDbType.Int).Value = toDocumentId;
        command.Parameters.Add("@fromId", SqlDbType.Int).Value = fromDocumentId;
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> CreateRemindersAsync(
        SqlConnection connection, SqlTransaction transaction,
        int documentId, int documentTypeId, DateOnly expiryDate, DateOnly today, CancellationToken ct)
    {
        // UNION прибирає дубль, якщо типовий відступ теж 7 днів. Дати в минулому не створюємо.
        const string sql = """
            INSERT INTO Reminders (DocumentId, DaysBefore, RemindOn, Channel, IsSent)
            SELECT @documentId, v.Days, DATEADD(DAY, -v.Days, @expiryDate), N'Email', 0
            FROM (
                SELECT DefaultReminderDays AS Days FROM DocumentTypes WHERE Id = @typeId
                UNION
                SELECT 7
            ) AS v
            WHERE DATEADD(DAY, -v.Days, @expiryDate) >= @today;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@documentId", SqlDbType.Int).Value = documentId;
        command.Parameters.Add("@typeId", SqlDbType.Int).Value = documentTypeId;
        command.Parameters.Add("@expiryDate", SqlDbType.Date).Value = expiryDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@today", SqlDbType.Date).Value = today.ToDateTime(TimeOnly.MinValue);
        return await command.ExecuteNonQueryAsync(ct);
    }

    // =====================================================================
    // Допоміжні методи: параметри та читання
    // =====================================================================

    private static void AddOwnerParameters(SqlCommand command, OwnerInput input)
    {
        command.Parameters.Add("@fullName", SqlDbType.NVarChar, 150).Value = input.FullName;
        command.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = input.Email;
        command.Parameters.Add("@birthDate", SqlDbType.Date).Value = ToDbDate(input.BirthDate);
    }

    private static void AddDocumentParameters(SqlCommand command, DocumentInput input)
    {
        command.Parameters.Add("@ownerId", SqlDbType.Int).Value = input.OwnerId;
        command.Parameters.Add("@documentTypeId", SqlDbType.Int).Value = input.DocumentTypeId;
        command.Parameters.Add("@number", SqlDbType.NVarChar, 50).Value = input.Number;
        command.Parameters.Add("@title", SqlDbType.NVarChar, 200).Value = input.Title;
        command.Parameters.Add("@issueDate", SqlDbType.Date).Value = input.IssueDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@expiryDate", SqlDbType.Date).Value = ToDbDate(input.ExpiryDate);
        // C# null → DBNull.Value
        command.Parameters.Add("@filePath", SqlDbType.NVarChar, 500).Value = (object?)input.FilePath ?? DBNull.Value;
        command.Parameters.Add("@notes", SqlDbType.NVarChar, 1000).Value = (object?)input.Notes ?? DBNull.Value;
    }

    private static object ToDbDate(DateOnly? date)
        => date is null ? DBNull.Value : (object)date.Value.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ReadNullableDate(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static OwnerDto ReadOwner(SqlDataReader reader)
        => new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), ReadNullableDate(reader, 3));

    private sealed record DocumentOrdinals(
        int Id, int OwnerId, int OwnerName, int TypeId, int TypeName, int Number,
        int Title, int IssueDate, int ExpiryDate, int Status, int FilePath, int Notes)
    {
        public static DocumentOrdinals From(SqlDataReader r) => new(
            r.GetOrdinal("Id"), r.GetOrdinal("OwnerId"), r.GetOrdinal("OwnerName"),
            r.GetOrdinal("DocumentTypeId"), r.GetOrdinal("TypeName"), r.GetOrdinal("Number"),
            r.GetOrdinal("Title"), r.GetOrdinal("IssueDate"), r.GetOrdinal("ExpiryDate"),
            r.GetOrdinal("Status"), r.GetOrdinal("FilePath"), r.GetOrdinal("Notes"));
    }

    private static DocumentDetailsDto ReadDocumentDetails(SqlDataReader r, DocumentOrdinals o) => new(
        r.GetInt32(o.Id),
        r.GetInt32(o.OwnerId),
        r.GetString(o.OwnerName),
        r.GetInt32(o.TypeId),
        r.GetString(o.TypeName),
        r.GetString(o.Number),
        r.GetString(o.Title),
        DateOnly.FromDateTime(r.GetDateTime(o.IssueDate)),
        ReadNullableDate(r, o.ExpiryDate),
        Enum.Parse<DocumentStatus>(r.GetString(o.Status)),
        r.IsDBNull(o.FilePath) ? null : r.GetString(o.FilePath),
        r.IsDBNull(o.Notes) ? null : r.GetString(o.Notes));
}
