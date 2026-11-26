using DocOrganizer.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.Api.Data;

/// <summary>
/// Контекст БД. Реєструється як Scoped (AddDbContext): один екземпляр на HTTP-запит,
/// бо DbContext не потокобезпечний і тримає Change Tracker конкретного запиту.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
