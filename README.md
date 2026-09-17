# CapOrderDemo

A small .NET 10 demonstration of [CAP](https://cap.dotnetcore.xyz/) using SQL Server for reliable message storage and Azure Service Bus as the message broker.

The solution models a simple order workflow:

```text
Order.Api -- order.submitted --> Azure Service Bus topic
    ^                                  |
    |                                  v
    +-- inventory.reservation.completed -- Inventory.Worker
```

`Order.Api` accepts orders and stores them as `Pending`. `Inventory.Worker` consumes the order event, reserves inventory, and publishes the result. The API consumes that result and changes the order to `Confirmed` or `Rejected`.

## Projects

- `src/Contracts`: Shared event contracts.
- `src/Order.Api`: ASP.NET Core API, order database, CAP publisher, and completion subscriber.
- `src/Inventory.Worker`: Background worker, inventory database, CAP subscriber, and retry demonstration.

CAP uses:

- SQL Server for CAP's `Published` and `Received` tables and application data.
- Azure Service Bus for transport.
- One Azure Service Bus topic named `cap-order-demo`.
- CAP subscription groups named `inventory-worker` and `order-api`.

## Prerequisites

Install or have access to:

- .NET SDK 10.
- SQL Server, Azure SQL, or SQL Server in a container.
- An Azure Service Bus namespace on the Standard or Premium tier. Basic tier does not support topics.
- Insomnia, curl, or another HTTP client.

The project uses EF Core migrations and the `dotnet-ef` tool. Install the matching tool if needed:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.12
```

## Infrastructure Setup

### SQL Server

Create two databases, one for each application. The database names are only examples; use any names you prefer.

```sql
CREATE DATABASE CapOrders;
CREATE DATABASE CapInventory;
```

The API and worker apply their EF Core migrations at startup. Those migrations create both the application tables and CAP's SQL Server tables.

### Azure Service Bus

Create an Azure Service Bus namespace on the Standard or Premium tier. You do not need to manually create the topic or subscriptions for this demo. CAP is configured with:

```text
Topic: cap-order-demo
Subscriptions: inventory-worker, order-api
```

`AutoProvision = true` allows CAP to create the topic and subscriptions when the applications start, provided the Service Bus credentials have permission to manage entities.

You can also create the topic and subscriptions manually if your environment does not allow application-side provisioning. The names must match the CAP configuration and subscriber groups.

## Configure User Secrets

Secrets are stored outside the repository by the .NET user-secrets provider. The project files contain only `UserSecretsId` identifiers, not secret values.

From the repository root, configure the API:

```powershell
dotnet user-secrets --project .\src\Order.Api\Order.Api.csproj set "ConnectionStrings:Database" "Server=localhost;Database=CapOrders;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet user-secrets --project .\src\Order.Api\Order.Api.csproj set "ConnectionStrings:ServiceBus" "<azure-service-bus-connection-string>"
```

Configure the worker with its database and the same Service Bus connection string:

```powershell
dotnet user-secrets --project .\src\Inventory.Worker\Inventory.Worker.csproj set "ConnectionStrings:Database" "Server=localhost;Database=CapInventory;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet user-secrets --project .\src\Inventory.Worker\Inventory.Worker.csproj set "ConnectionStrings:ServiceBus" "<azure-service-bus-connection-string>"
```

For SQL authentication, use a connection string appropriate for your local SQL Server, for example:

```text
Server=localhost;Database=CapOrders;User Id=<user>;Password=<password>;TrustServerCertificate=True;
```

Verify the configured keys without committing their values:

```powershell
dotnet user-secrets --project .\src\Order.Api\Order.Api.csproj list
dotnet user-secrets --project .\src\Inventory.Worker\Inventory.Worker.csproj list
```

Never put the Service Bus connection string or SQL password in `appsettings.json`, `appsettings.Development.json`, source code, or committed SQL files.

## Run the Demo

Open two terminals from the repository root.

Start the API:

```powershell
dotnet run --project .\src\Order.Api\Order.Api.csproj
```

The API listens on:

```text
http://localhost:5147
```

Start the worker in the second terminal:

```powershell
dotnet run --project .\src\Inventory.Worker\Inventory.Worker.csproj
```

The worker is a background process and does not expose an HTTP endpoint. Keep both processes running.

On first startup, each application applies its pending EF Core migrations. CAP also provisions the Service Bus topic and subscriptions if the connection string has sufficient permissions.

## Try an Order

Create an order using Insomnia or curl:

```http
POST http://localhost:5147/orders
Content-Type: application/json
```

```json
{
  "productId": 1,
  "quantity": 2
}
```

The API returns `202 Accepted` with an order ID and an initial `Pending` status. Use that ID to query the order:

```http
GET http://localhost:5147/orders/{orderId}
```

After the worker processes the event, the order should become `Confirmed`.

The seeded products are:

| Product ID | Name | Initial quantity |
| --- | --- | ---: |
| 1 | Mechanical Keyboard | 10 |
| 2 | Wireless Mouse | 5 |

To test a normal inventory rejection, request more stock than is available:

```json
{
  "productId": 1,
  "quantity": 999
}
```

The order should become `Rejected` with an `Insufficient inventory.` failure reason.

## CAP Dashboard

The API enables the CAP dashboard. Open:

```text
http://localhost:5147/cap
```

The dashboard shows published and received messages, retries, failed messages, and subscriber groups. The exact page contents depend on which messages are still retained by CAP's SQL storage cleanup policy.

## Test CAP Retries

The worker has a development-only failure simulator in `src/Inventory.Worker/appsettings.Development.json`:

```json
"CapDemo": {
  "FailureSimulation": {
    "Enabled": false,
    "ProductId": 2,
    "FailFirstAttempts": 2
  }
}
```

To test retries:

1. Set `Enabled` to `true`.
2. Restart the worker so it reloads configuration.
3. Submit a new order for `productId` 2.
4. Watch the worker logs and CAP dashboard.
5. Set `Enabled` back to `false` after the experiment.

The simulator throws before the inventory transaction commits. CAP retries the failed subscriber invocation. After the configured number of simulated failures, the handler succeeds and publishes the completion event.

The worker currently uses:

```csharp
options.FailedRetryCount = 5;
```

CAP performs several immediate retries, then uses its retry processor. `FailedRetryInterval` controls the retry processor polling interval, while `FallbackWindowLookbackSeconds` is a lookback window rather than an exponential backoff schedule. These options can be configured in the `AddCap` block in `src/Inventory.Worker/Program.cs`.

Keep `FailFirstAttempts` below `FailedRetryCount` when you want the simulated message to eventually succeed. If CAP exhausts its retries, the CAP message becomes failed and the order remains `Pending` because no completion event is published.

## Useful SQL Queries

`SQL_Queries.sql` contains queries for inspecting:

- CAP `Published` and `Received` records.
- Orders.
- Inventory reservations.
- Product quantities.

The application databases use the schemas and tables created by the migrations. Do not edit CAP's rows manually while the applications are running unless you are intentionally experimenting with recovery behavior.

## Troubleshooting

### `Invalid object name 'Orders'`

The API database has not been migrated. Restart the API. It applies migrations during startup. Confirm that the configured `Database` connection string points to the expected database.

### Worker exits immediately

Make sure the worker contains its host startup code and run it with `dotnet run`. A healthy worker remains running and logs its background activity.

### Build fails because a DLL is locked

Stop the running API or worker with `Ctrl+C` before rebuilding. A running process can lock files such as `Contracts.dll` and `Inventory.Worker.dll`.

If the process does not stop cleanly, find the process and terminate it from PowerShell:

```powershell
Get-Process -Name Inventory.Worker,Order.Api
Stop-Process -Name Inventory.Worker -Force
Stop-Process -Name Order.Api -Force
```

You can also terminate one specific process by PID:

```powershell
Stop-Process -Id <process-id> -Force
```

### No messages appear in Azure Service Bus

Messages are held by subscriptions, not directly by the topic. Successful messages may disappear quickly because the worker or API has consumed and completed them. Stop the relevant consumer, send a new order, and use Azure Portal's **Peek** operation to inspect a pending message.

### Order remains `Pending`

Check both application logs and the CAP dashboard. A pending order may have a failed CAP message, may have been created before a subscriber existed, or may have been sent while a service was unavailable. CAP does not automatically replay old business events after a new subscriber is added.

## Git and Secrets

The repository ignores .NET build output and local IDE files. Before pushing, check:

```powershell
git status
git diff --cached --check
```

Only commit source, migrations, project files, non-secret configuration templates, and documentation. Keep user-secrets values in the local .NET user-secrets store.
