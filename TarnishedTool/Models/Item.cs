// 

namespace TarnishedTool.Models;

public class Item
{
    public bool IsDlc { get; set; }
    public bool IsTarnishedPack { get; set; }
    public int Id { get; set; }
    public string Name { get; set; }
    public int StackSize { get; set; } 
    public int MaxStorage { get; set; }
    public string CategoryName { get; set; }
}