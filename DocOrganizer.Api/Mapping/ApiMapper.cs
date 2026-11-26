using DocOrganizer.Api.Contracts;
using DocOrganizer.Api.Domain;
using Riok.Mapperly.Abstractions;

namespace DocOrganizer.Api.Mapping;

/// <summary>
/// Мапінг «модель ↔ DTO» на основі Mapperly (код генерується під час компіляції, без рефлексії).
/// Клас без стану, тому реєструється як Singleton.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ApiMapper
{
    // ── Owner ──
    [MapperIgnoreTarget(nameof(Owner.Id))]
    [MapperIgnoreTarget(nameof(Owner.AccountId))]   // береться з токена, а не з тіла запиту
    [MapperIgnoreTarget(nameof(Owner.Documents))]
    public partial Owner ToEntity(CreateOwnerDto dto);

    [MapperIgnoreTarget(nameof(Owner.Id))]
    [MapperIgnoreTarget(nameof(Owner.AccountId))]
    [MapperIgnoreTarget(nameof(Owner.Documents))]
    public partial void Apply(UpdateOwnerDto dto, Owner target);

    // ── DocumentType ──
    public partial DocumentTypeDto ToDto(DocumentType entity);

    [MapperIgnoreTarget(nameof(DocumentType.Id))]
    [MapperIgnoreTarget(nameof(DocumentType.Documents))]
    public partial DocumentType ToEntity(SaveDocumentTypeDto dto);

    [MapperIgnoreTarget(nameof(DocumentType.Id))]
    [MapperIgnoreTarget(nameof(DocumentType.Documents))]
    public partial void Apply(SaveDocumentTypeDto dto, DocumentType target);

    /// <summary>Проєкція IQueryable → DTO: Mapperly генерує Select, який EF Core перекладає в SQL.</summary>
    public partial IQueryable<DocumentTypeDto> ProjectToDto(IQueryable<DocumentType> query);

    // ── Tag ──
    public partial TagDto ToDto(Tag entity);
    public partial IQueryable<TagDto> ProjectToDto(IQueryable<Tag> query);

    // ── Reminder ──
    public partial ReminderDto ToDto(Reminder entity);
    public partial IQueryable<ReminderDto> ProjectToDto(IQueryable<Reminder> query);

    // ── Document ──
    [MapperIgnoreTarget(nameof(Document.Id))]
    [MapperIgnoreTarget(nameof(Document.Owner))]
    [MapperIgnoreTarget(nameof(Document.DocumentType))]
    [MapperIgnoreTarget(nameof(Document.Status))]
    [MapperIgnoreTarget(nameof(Document.CreatedAt))]
    [MapperIgnoreTarget(nameof(Document.Version))]
    [MapperIgnoreTarget(nameof(Document.Reminders))]
    [MapperIgnoreTarget(nameof(Document.DocumentTags))]
    [MapperIgnoreSource(nameof(CreateDocumentDto.TagIds))]
    [MapperIgnoreSource(nameof(CreateDocumentDto.CreateDefaultReminder))]
    public partial Document ToEntity(CreateDocumentDto dto);

    /// <summary>
    /// Документ → DTO. Частина полів обчислюється (стан строку дії, днів до завершення),
    /// частина «сплющується» з навігаційних властивостей, Version перетворюється на base64 —
    /// тому метод написано вручну всередині Mapperly-класу.
    /// </summary>
    public DocumentDto ToDto(Document d, DateOnly today) => new(
        d.Id,
        d.OwnerId,
        d.Owner.FullName,
        d.DocumentTypeId,
        d.DocumentType.Name,
        d.Number,
        d.Title,
        d.IssueDate,
        d.ExpiryDate,
        d.IssuedBy,
        d.Status,
        ExpiryRules.GetState(d.ExpiryDate, d.Status, today),
        ExpiryRules.DaysLeft(d.ExpiryDate, today),
        d.FilePath,
        d.Notes,
        d.DocumentTags.Select(t => t.Tag.Name).Order().ToList(),
        d.CreatedAt,
        Convert.ToBase64String(d.Version));

    public DocumentListItemDto ToListItem(Document d, DateOnly today) => new(
        d.Id, d.OwnerId, d.Owner.FullName, d.DocumentType.Name, d.Number, d.Title, d.ExpiryDate, d.Status,
        ExpiryRules.GetState(d.ExpiryDate, d.Status, today),
        ExpiryRules.DaysLeft(d.ExpiryDate, today));
}
