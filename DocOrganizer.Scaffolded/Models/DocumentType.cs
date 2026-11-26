using System;
using System.Collections.Generic;

namespace DocOrganizer.Scaffolded.Models;

public partial class DocumentType
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int DefaultReminderDays { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
