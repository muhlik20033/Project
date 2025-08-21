using System.Text;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Domain;
using Shared.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["RabbitMq:Host"] = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq",
    ["RabbitMq:User"] = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
    ["RabbitMq:Pass"] = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "guest",
    ["RabbitMq:Queue"] = Environment.GetEnvironmentVariable("RABBITMQ_QUEUE") ?? "grouping.start",
    ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION") ?? "Host=postgres;Port=5432;Database=goods;Username=postgres;Password=postgres"
});

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<Repository>();
builder.Services.AddScoped<Grouper>();

builder.Services.AddHostedService<QueueConsumerService>();
builder.Services.AddHostedService<PeriodicScanService>();

var app = builder.Build();
app.Run();

// Consumes StartGroupingMessage and runs Grouper
public class QueueConsumerService(IServiceProvider sp, IConfiguration cfg) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            var factory = new ConnectionFactory
            {
                HostName = cfg["RabbitMq:Host"],
                UserName = cfg["RabbitMq:User"],
                Password = cfg["RabbitMq:Pass"]
            };
            using var conn = factory.CreateConnection();
            using var channel = conn.CreateModel();
            var queue = cfg["RabbitMq:Queue"]!;
            channel.QueueDeclare(queue, true, false, false);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (_, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var msg = System.Text.Json.JsonSerializer.Deserialize<StartGroupingMessage>(json);
                    if (msg is null) return;
                    using var scope = sp.CreateScope();
                    var grouper = scope.ServiceProvider.GetRequiredService<Grouper>();
                    await grouper.CreateGroupsAsync(msg.BatchId);
                    channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    // requeue on error
                    channel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            channel.BasicConsume(queue, autoAck: false, consumer);
            Console.WriteLine("Worker listening to queue " + queue);
            while (!stoppingToken.IsCancellationRequested)
                Thread.Sleep(1000);
        }, stoppingToken);
    }
}

// Every 5 minutes scans for batches with pending items and triggers grouping
public class PeriodicScanService(IServiceProvider sp) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = sp.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<Repository>();
                var db = repo.Db;
                var batches = db.Batches.ToList();
                foreach (var b in batches)
                {
                    var hasPending = db.ProductItems.Any(i => i.BatchId == b.Id && i.QuantityRemaining > 0);
                    if (hasPending)
                    {
                        var g = scope.ServiceProvider.GetRequiredService<Grouper>();
                        await g.CreateGroupsAsync(b.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
