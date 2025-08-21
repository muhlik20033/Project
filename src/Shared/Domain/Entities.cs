using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shared.Domain;

public enum ItemStatus { Pending = 0, Processed = 1 }

public class UploadBatch
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? FileName { get; set; }
    public int ItemsCount { get; set; }
    public bool GroupingCompleted { get; set; }
}

public class ProductItem
{
    [Key] public long Id { get; set; }
    public Guid BatchId { get; set; }
    public string Name { get; set; } = default!;
    public string Unit { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public int QuantityTotal { get; set; }
    public int QuantityRemaining { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Pending;
}

public class GoodsGroup
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public string Title { get; set; } = default!;
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<GroupItem> Items { get; set; } = new();
}

public class GroupItem
{
    [Key] public long Id { get; set; }
    public Guid GroupId { get; set; }
    public long ProductItemId { get; set; }
    public string ProductName { get; set; } = default!;
    public string Unit { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal => UnitPrice * Quantity;
}
