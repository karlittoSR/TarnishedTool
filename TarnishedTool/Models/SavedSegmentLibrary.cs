//

using System.Collections.Generic;

namespace TarnishedTool.Models;

// Internal saved-segment library format. Folders are stored separately and
// referenced by stable ids, so renaming or moving a folder never rewrites paths
// on every segment. The SavedLine itself stays unchanged and therefore keeps the
// existing single-segment export format clean.
public class SavedSegmentLibrary
{
    public const string FormatId = "TarnishedTool.SavedSegmentLibrary";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatId;
    public int Version { get; set; } = CurrentVersion;
    public List<SavedSegmentFolder> Folders { get; set; } = new();
    public List<SavedSegmentLibraryEntry> Segments { get; set; } = new();
}

public class SavedSegmentFolder
{
    public string Id { get; set; }
    public string ParentId { get; set; }
    public string Name { get; set; }
    public int Order { get; set; }
}

public class SavedSegmentLibraryEntry
{
    // Null means the library root.
    public string FolderId { get; set; }
    public int Order { get; set; }
    public SavedLine Segment { get; set; }
}
