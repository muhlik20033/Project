using Shared.Domain;
using Shared.Infrastructure;

namespace Shared.Infrastructure;

public class Grouper(Repository repo)
{
    public const decimal Limit = 200m;

    public async Task<List<GoodsGroup>> CreateGroupsAsync(Guid batchId)
    {
        var groups = new List<GoodsGroup>();
        var items = await repo.GetPendingItemsAsync(batchId);

        int groupIndex = 1;
        while (items.Any(i => i.QuantityRemaining > 0))
        {
            var group = new GoodsGroup
            {
                BatchId = batchId,
                Title = $"Группа {groupIndex}"
            };

            decimal total = 0m;
            // Greedy "best fit": always pick the most expensive item that still fits, try to get as close to 200 as possible.
            while (true)
            {
                var candidate = FindBestCandidate(items, Limit - total);
                if (candidate is null) break;

                // add 1 unit of the chosen item
                candidate.QuantityRemaining -= 1;
                var gi = new GroupItem
                {
                    GroupId = group.Id,
                    ProductItemId = candidate.Id,
                    ProductName = candidate.Name,
                    Unit = candidate.Unit,
                    UnitPrice = candidate.UnitPrice,
                    Quantity = 1
                };
                var existing = group.Items.FirstOrDefault(x => x.ProductItemId == gi.ProductItemId);
                if (existing is null) group.Items.Add(gi);
                else existing.Quantity += 1;

                total += candidate.UnitPrice;
                if (total >= Limit) break;
            }

            group.TotalPrice = total;
            groups.Add(group);
            groupIndex++;

            // refresh items list (remove zeros)
            items = items.Where(i => i.QuantityRemaining > 0).OrderByDescending(i => i.UnitPrice).ToList();
            if (!items.Any()) break;
        }

        // Mark items as processed when QuantityRemaining==0
        foreach (var it in repo.Db.ProductItems.Where(p => p.BatchId == batchId))
        {
            if (it.QuantityRemaining <= 0) it.Status = ItemStatus.Processed;
        }

        repo.Db.GoodsGroups.AddRange(groups);
        await repo.SaveChangesAsync();
        return groups;
    }

    private static ProductItem? FindBestCandidate(List<ProductItem> items, decimal remaining)
    {
        if (remaining <= 0m) return null;
        // pick the most expensive item that still fits
        foreach (var it in items.OrderByDescending(i => i.UnitPrice))
        {
            if (it.QuantityRemaining > 0 && it.UnitPrice <= remaining) return it;
        }
        // If nothing fits, return null
        return null;
    }
}
