using System;
using System.Collections.Generic;

namespace DocOrganizer.Scaffolded.Models;

public partial class Tag
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();
}
