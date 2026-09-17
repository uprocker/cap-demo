using Inventory.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Database")));

builder.Services.AddCap(options =>
{
    options.UseEntityFramework<InventoryDbContext>();

    options.UseAzureServiceBus(serviceBus =>
    {
        serviceBus.ConnectionString =
            builder.Configuration.GetConnectionString("ServiceBus")!;
        serviceBus.TopicPath = "cap-order-demo";
        serviceBus.AutoProvision = true;
    });

    options.DefaultGroupName = "inventory-worker";
    options.FailedRetryCount = 5;
});

builder.Services.AddScoped<OrderSubmittedHandler>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();