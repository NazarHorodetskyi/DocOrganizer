using System;
using System.Collections.Generic;

namespace DocOrganizer.Scaffolded.Models;

public partial class Owner
{
    public int Id { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public DateOnly? BirthDate { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
