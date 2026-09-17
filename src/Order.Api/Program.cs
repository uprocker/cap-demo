using Microsoft.EntityFrameworkCore;
using Order.Api;
using DotNetCore.CAP;
using Contracts;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Database")));

builder.Services.AddCap(options =>
{
    options.UseEntityFramework<OrderDbContext>();

    options.UseAzureServiceBus(serviceBus =>
    {
        serviceBus.ConnectionString =
            builder.Configuration.GetConnectionString("ServiceBus")!;
        serviceBus.TopicPath = "cap-order-demo";
        serviceBus.AutoProvision = true;
    });

    options.UseDashboard();
    options.DefaultGroupName = "order-api";
    options.FailedRetryCount = 5;
});

builder.Services.AddScoped<InventoryReservationCompletedHandler>();

var app = builder.Build();

// Apply both the application schema and CAP's SQL tables before serving requests.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.MigrateAsync();
}

app.MapPost("/orders", CreateOrderAsync);
app.MapGet("/orders/{id:guid}", GetOrderAsync);

app.Run();

static async Task<IResult> CreateOrderAsync(
    CreateOrderRequest request,
    OrderDbContext db,
    ICapPublisher publisher)
{
    if (request.ProductId <= 0)
    {
        return Results.BadRequest(new { error = "ProductId must be greater than zero." });
    }

    if (request.Quantity <= 0)
    {
        return Results.BadRequest(new { error = "Quantity must be greater than zero." });
    }

    var order = new global::Order.Api.Order
    {
        Id = Guid.NewGuid(),
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        Status = OrderStatus.Pending,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    // CAP stores the event until the order transaction commits successfully.
    await using var transaction = await db.Database.BeginTransactionAsync(publisher);

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    await publisher.PublishAsync("order.submitted", new OrderSubmitted(
        OrderId: order.Id,
        ProductId: order.ProductId,
        Quantity: order.Quantity,
        SubmittedAtUtc: DateTime.UtcNow));

    await transaction.CommitAsync();

    return Results.Accepted(
        $"/orders/{order.Id}",
        new
        {
            order.Id,
            order.ProductId,
            order.Quantity,
            status = order.Status.ToString()
        });
}

static async Task<IResult> GetOrderAsync(
    Guid id,
    OrderDbContext db)
{
    var order = await db.Orders
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == id);

    return order is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            order.Id,
            order.ProductId,
            order.Quantity,
            status = order.Status.ToString(),
            order.FailureReason,
            order.CreatedAtUtc,
            order.UpdatedAtUtc
        });
}
