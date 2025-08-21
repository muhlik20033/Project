using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using RabbitMQ.Client;
using Shared.Domain;
using Shared.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using ClosedXML.Excel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<Repository>();
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(builder.Configuration["Mongo:ConnectionString"]));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(builder.Configuration["Mongo:Database"]));
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = builder.Configuration["Redis:Configuration"]);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Goods Grouper API", Version = "v1" });
});

builder.Services.AddCors(o =>
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

// Ensure DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// RabbitMQ connection factory
ConnectionFactory CreateFactory() => new()
{
    HostName = builder.Configuration["RabbitMq:Host"],
    UserName = builder.Configuration["RabbitMq:User"],
    Password = builder.Configuration["RabbitMq:Pass"]
};

IConnection GetConnection() => CreateFactory().CreateConnection();

app.MapPost("/api/upload", async (HttpRequest request, Repository repo, IMongoDatabase mongo) =>
{
    if (!request.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null) return Results.BadRequest(".xlsx file is required");

    if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only .xlsx accepted");

    // Parse Excel
    var items = new List<ProductItem>();
    using var stream = file.OpenReadStream();
    using var workbook = new XLWorkbook(stream);
    var ws = workbook.Worksheets.First();

    // Expect headers in row 1: Наименование | Единица измерения | Цена за единицу, евро | Количество, шт.
    var lastRow = ws.LastRowUsed().RowNumber();
    for (int r = 2; r <= lastRow; r++)
    {
        string name = ws.Cell(r, 1).GetString().Trim();
        string unit = ws.Cell(r, 2).GetString().Trim();
        decimal price = ws.Cell(r, 3).GetDecimal();
        int qty = (int)ws.Cell(r, 4).GetDouble();

        items.Add(new ProductItem
        {
            Name = name,
            Unit = unit,
            UnitPrice = price,
            QuantityTotal = qty,
            QuantityRemaining = qty
        });
    }

    var batch = new UploadBatch
    {
        FileName = file.FileName,
        ItemsCount = items.Count
    };
    repo.Db.Batches.Add(batch);
    await repo.SaveChangesAsync();

    foreach (var it in items)
    {
        it.BatchId = batch.Id;
        repo.Db.ProductItems.Add(it);
    }
    await repo.SaveChangesAsync();

    // Save raw meta to Mongo
    var uploads = mongo.GetCollection<dynamic>("uploads");
    await uploads.InsertOneAsync(new
    {
        batchId = batch.Id,
        file = file.FileName,
        at = DateTime.UtcNow,
        count = items.Count
    });

    // Publish RMQ message
    using var conn = GetConnection();
    using var channel = conn.CreateModel();
    var queue = builder.Configuration["RabbitMq:Queue"]!;
    channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
    var msg = System.Text.Json.JsonSerializer.Serialize(new StartGroupingMessage(batch.Id));
    var body = Encoding.UTF8.GetBytes(msg);
    channel.BasicPublish(exchange: "", routingKey: queue, basicProperties: null, body: body);

    return Results.Ok(new { batchId = batch.Id, message = "Uploaded and queued for grouping." });
})
.Accepts<IFormFile>("multipart/form-data")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

app.MapGet("/api/groups", async (Repository repo, IDistributedCache cache) =>
{
    var cached = await cache.GetStringAsync("groups:latest");
    if (cached is not null) return Results.Text(cached, "application/json");

    var groups = repo.Db.GoodsGroups
        .OrderBy(g => g.CreatedAt)
        .Select(g => new { g.Id, g.Title, g.TotalPrice, g.CreatedAt, g.BatchId })
        .ToList();

    var json = System.Text.Json.JsonSerializer.Serialize(groups);
    await cache.SetStringAsync("groups:latest", json);
    return Results.Text(json, "application/json");
});

app.MapGet("/api/groups/{id:guid}/items", (Guid id, Repository repo) =>
{
    var items = repo.Db.GroupItems.Where(x => x.GroupId == id)
        .Select(x => new
        {
            x.ProductName,
            x.Unit,
            x.UnitPrice,
            x.Quantity,
            Subtotal = x.UnitPrice * x.Quantity
        }).ToList();
    return Results.Ok(items);
});

app.Run();
