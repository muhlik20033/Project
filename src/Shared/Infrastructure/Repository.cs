using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace Shared.Infrastructure;

public class Repository(AppDbContext db)
{
    public async Task<UploadBatch?> GetBatchAsync(Guid id) => await db.Batches.FindAsync(id);

    public async Task<List<ProductItem>> GetPendingItemsAsync(Guid batchId) =>
        await db.ProductItems.Where(p => p.BatchId == batchId && p.QuantityRemaining > 0)
            .OrderByDescending(p => p.UnitPrice)
            .ToListAsync();

    public async Task SaveChangesAsync() => await db.SaveChangesAsync();

    public AppDbContext Db => db;
}
