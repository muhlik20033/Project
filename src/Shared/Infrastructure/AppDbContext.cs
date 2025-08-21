using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace Shared.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UploadBatch> Batches => Set<UploadBatch>();
    public DbSet<ProductItem> ProductItems => Set<ProductItem>();
    public DbSet<GoodsGroup> GoodsGroups => Set<GoodsGroup>();
    public DbSet<GroupItem> GroupItems => Set<GroupItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductItem>().HasIndex(p => new { p.BatchId, p.Status });
        modelBuilder.Entity<GoodsGroup>().HasMany(g => g.Items).WithOne().HasForeignKey(i => i.GroupId);
    }
}
