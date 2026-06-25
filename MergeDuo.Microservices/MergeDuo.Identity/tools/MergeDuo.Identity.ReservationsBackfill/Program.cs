using MergeDuo.Identity.Domain.Documents;
using MergeDuo.Identity.Domain.Options;
using MergeDuo.Identity.Domain.Rules;
using MergeDuo.Identity.Infra.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

var apply = args.Any(x => string.Equals(x, "--apply", StringComparison.OrdinalIgnoreCase));
var dryRun = !apply;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var cosmosOptions = configuration.GetSection("Cosmos").Get<CosmosOptions>() ?? new CosmosOptions();
using var client = CosmosClientFactory.Create(cosmosOptions);
var users = client.GetContainer(cosmosOptions.Database, cosmosOptions.UsersContainer);
var reservations = client.GetContainer(cosmosOptions.Database, cosmosOptions.IdentityReservationsContainer);

var allUsers = await ListUsersAsync(users, CancellationToken.None);
var rows = allUsers
    .SelectMany(user => IdentityReservationRules.ForUser(user).Select(value => new ReservationRow(user, value)))
    .ToArray();

var duplicates = rows
    .GroupBy(x => x.Value.Id, StringComparer.Ordinal)
    .Where(group => group.Select(x => x.User.Id).Distinct(StringComparer.Ordinal).Count() > 1)
    .ToArray();

if (duplicates.Length > 0)
{
    Console.Error.WriteLine("Duplicate identity values found. No reservations were written.");
    foreach (var duplicate in duplicates)
    {
        var usersForValue = string.Join(", ", duplicate.Select(x => x.User.Id).Distinct(StringComparer.Ordinal));
        Console.Error.WriteLine($"{duplicate.First().Value.Kind}:{duplicate.First().Value.ValueHash} -> {usersForValue}");
    }

    return 2;
}

Console.WriteLine($"Users={allUsers.Count} reservations={rows.Length} dryRun={dryRun}");
if (dryRun)
{
    Console.WriteLine("Dry-run completed. Re-run with --apply to create active reservations.");
    return 0;
}

var created = 0;
var skipped = 0;
foreach (var row in rows)
{
    var now = DateTimeOffset.UtcNow;
    var document = IdentityReservationRules.ToDocument(
        row.Value,
        row.User.Id,
        IdentityReservationRules.StatusActive,
        now);

    try
    {
        await reservations.CreateItemAsync(document, new PartitionKey(document.Id));
        created++;
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        var existing = await reservations.ReadItemAsync<IdentityReservationDocument>(
            document.Id,
            new PartitionKey(document.Id));

        if (existing.Resource.UserId == document.UserId &&
            existing.Resource.Status == IdentityReservationRules.StatusActive)
        {
            skipped++;
            continue;
        }

        Console.Error.WriteLine(
            $"Reservation conflict for {document.Kind}:{document.ValueHash}. Existing user={existing.Resource.UserId}, status={existing.Resource.Status}, target user={document.UserId}.");
        return 3;
    }
}

Console.WriteLine($"Backfill completed. created={created} skipped={skipped}");
return 0;

static async Task<IReadOnlyList<UserDocument>> ListUsersAsync(Container users, CancellationToken cancellationToken)
{
    var query = new QueryDefinition("SELECT * FROM c WHERE c.docType = 'user'");
    using var iterator = users.GetItemQueryIterator<UserDocument>(
        query,
        requestOptions: new QueryRequestOptions { MaxItemCount = 100 });

    var results = new List<UserDocument>();
    while (iterator.HasMoreResults)
    {
        var page = await iterator.ReadNextAsync(cancellationToken);
        results.AddRange(page.Resource);
    }

    return results;
}

file sealed record ReservationRow(UserDocument User, IdentityReservationValue Value);
