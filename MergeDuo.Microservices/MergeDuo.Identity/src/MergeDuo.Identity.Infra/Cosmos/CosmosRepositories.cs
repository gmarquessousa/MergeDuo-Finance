using System.Net;
using Azure.Identity;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Documents;
using MergeDuo.Identity.Domain.Options;
using MergeDuo.Identity.Domain.Rules;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Identity.Infra.Cosmos;

public sealed class UsersRepository : IUsersRepository
{
    private readonly Container _container;
    private readonly Container _reservations;

    public UsersRepository(CosmosClient client, CosmosOptions options)
    {
        _container = client.GetContainer(options.Database, options.UsersContainer);
        _reservations = client.GetContainer(options.Database, options.IdentityReservationsContainer);
    }

    public async Task<UserDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<UserDocument>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<UserDocument?> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken)
    {
        var reservationValue = IdentityReservationRules.For(IdentityReservationRules.KindGoogleSub, googleSub);
        var reservation = await ReadReservationAsync(reservationValue.Id, cancellationToken);
        if (reservation is { Status: IdentityReservationRules.StatusActive })
        {
            var reservedUser = await GetByIdAsync(reservation.UserId, cancellationToken);
            if (reservedUser?.Auth.Google.Sub == googleSub)
            {
                return reservedUser;
            }
        }

        var query = new QueryDefinition("SELECT * FROM c WHERE c.docType = 'user' AND c.auth.google.sub = @sub")
            .WithParameter("@sub", googleSub);

        using var iterator = _container.GetItemQueryIterator<UserDocument>(
            query,
            requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            var user = page.Resource.FirstOrDefault();
            if (user is not null)
            {
                return user;
            }
        }

        return null;
    }

    public async Task UpdateLoginSnapshotAsync(
        string userId,
        string googleEmail,
        bool googleEmailVerified,
        string? googlePictureUrl,
        string? googleHostedDomain,
        DateTimeOffset lastLoginAt,
        CancellationToken cancellationToken)
    {
        var patch = new[]
        {
            PatchOperation.Set("/auth/google/email", googleEmail),
            PatchOperation.Set("/auth/google/emailVerified", googleEmailVerified),
            PatchOperation.Set("/auth/google/pictureUrl", googlePictureUrl),
            PatchOperation.Set("/auth/google/hostedDomain", googleHostedDomain),
            PatchOperation.Set("/auth/lastLoginAt", lastLoginAt),
            PatchOperation.Set("/updatedAt", lastLoginAt)
        };

        await _container.PatchItemAsync<UserDocument>(
            userId,
            new PartitionKey(userId),
            patch,
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<UserDocument>> ListAllAsync(CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.docType = 'user'");
        using var iterator = _container.GetItemQueryIterator<UserDocument>(
            query,
            requestOptions: new QueryRequestOptions { MaxItemCount = 100 });

        var users = new List<UserDocument>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            users.AddRange(page.Resource);
        }

        return users;
    }

    public async Task CreateAsync(UserDocument user, CancellationToken cancellationToken)
    {
        if (await HandleExistsAsync(user.Handle, user.Id, cancellationToken) ||
            await EmailExistsAsync(user.Email, user.Id, cancellationToken) ||
            await GoogleSubExistsAsync(user.Auth.Google.Sub, user.Id, cancellationToken))
        {
            throw new IdentityConflictException("unique_key_conflict", "User unique key conflict.");
        }

        var reservations = IdentityReservationRules.ForUser(user);
        var pending = new List<IdentityReservationValue>();
        try
        {
            foreach (var reservation in reservations)
            {
                await ReserveAsync(reservation, user.Id, pending, cancellationToken);
            }

            await _container.CreateItemAsync(user, new PartitionKey(user.Id), cancellationToken: cancellationToken);
            foreach (var reservation in reservations)
            {
                await MarkReservationAsync(reservation, user.Id, IdentityReservationRules.StatusActive, cancellationToken);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            await DeletePendingAsync(pending, user.Id, cancellationToken);
            throw new IdentityConflictException("unique_key_conflict", "User unique key conflict.");
        }
        catch
        {
            await DeletePendingAsync(pending, user.Id, cancellationToken);
            throw;
        }
    }

    public async Task UpdateAsync(UserDocument user, string? ifMatchEtag, CancellationToken cancellationToken)
    {
        var current = await GetByIdAsync(user.Id, cancellationToken);
        if (await HandleExistsAsync(user.Handle, user.Id, cancellationToken))
        {
            throw new IdentityConflictException("handle_already_taken", "Handle already taken.");
        }

        if (await EmailExistsAsync(user.Email, user.Id, cancellationToken) ||
            await GoogleSubExistsAsync(user.Auth.Google.Sub, user.Id, cancellationToken))
        {
            throw new IdentityConflictException("unique_key_conflict", "User unique key conflict.");
        }

        var newReservations = IdentityReservationRules.ForUser(user);
        var oldReservations = current is null
            ? Array.Empty<IdentityReservationValue>()
            : IdentityReservationRules.ForUser(current);
        var pending = new List<IdentityReservationValue>();
        try
        {
            foreach (var reservation in newReservations)
            {
                await ReserveAsync(reservation, user.Id, pending, cancellationToken);
            }

            var options = string.IsNullOrWhiteSpace(ifMatchEtag)
                ? null
                : new ItemRequestOptions { IfMatchEtag = ifMatchEtag };
            await _container.ReplaceItemAsync(
                user,
                user.Id,
                new PartitionKey(user.Id),
                options,
                cancellationToken);

            foreach (var reservation in newReservations)
            {
                await MarkReservationAsync(reservation, user.Id, IdentityReservationRules.StatusActive, cancellationToken);
            }

            foreach (var reservation in oldReservations.Where(x => newReservations.All(y => y.Id != x.Id)))
            {
                await MarkReservationAsync(reservation, user.Id, IdentityReservationRules.StatusReleased, cancellationToken);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            await DeletePendingAsync(pending, user.Id, cancellationToken);
            throw new IdentityConflictException("handle_already_taken", "Handle already taken.");
        }
        catch
        {
            await DeletePendingAsync(pending, user.Id, cancellationToken);
            throw;
        }
    }

    private async Task ReserveAsync(
        IdentityReservationValue value,
        string userId,
        List<IdentityReservationValue> pending,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var document = IdentityReservationRules.ToDocument(
            value,
            userId,
            IdentityReservationRules.StatusPending,
            now);

        try
        {
            await _reservations.CreateItemAsync(
                document,
                new PartitionKey(document.Id),
                cancellationToken: cancellationToken);
            pending.Add(value);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await ReadReservationAsync(value.Id, cancellationToken);
            if (existing is not null &&
                existing.UserId == userId &&
                existing.Status == IdentityReservationRules.StatusActive)
            {
                return;
            }

            throw new IdentityConflictException(
                value.Kind == IdentityReservationRules.KindHandle ? "handle_already_taken" : "unique_key_conflict",
                "Identity reservation already exists.");
        }
    }

    private async Task<IdentityReservationDocument?> ReadReservationAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _reservations.ReadItemAsync<IdentityReservationDocument>(
                id,
                new PartitionKey(id),
                cancellationToken: cancellationToken);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task MarkReservationAsync(
        IdentityReservationValue value,
        string userId,
        string status,
        CancellationToken cancellationToken)
    {
        var existing = await ReadReservationAsync(value.Id, cancellationToken);
        if (existing is null || existing.UserId != userId)
        {
            return;
        }

        if (existing.Status == status)
        {
            return;
        }

        existing.Status = status;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _reservations.ReplaceItemAsync(
            existing,
            existing.Id,
            new PartitionKey(existing.Id),
            cancellationToken: cancellationToken);
    }

    private async Task DeletePendingAsync(
        IReadOnlyCollection<IdentityReservationValue> pending,
        string userId,
        CancellationToken cancellationToken)
    {
        foreach (var reservation in pending)
        {
            var existing = await ReadReservationAsync(reservation.Id, cancellationToken);
            if (existing is null ||
                existing.UserId != userId ||
                existing.Status != IdentityReservationRules.StatusPending)
            {
                continue;
            }

            try
            {
                await _reservations.DeleteItemAsync<IdentityReservationDocument>(
                    existing.Id,
                    new PartitionKey(existing.Id),
                    cancellationToken: cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
    }

    private Task<bool> HandleExistsAsync(string handle, string userIdToExclude, CancellationToken cancellationToken) =>
        AnyUserAsync(
            new QueryDefinition(
                    """
                    SELECT VALUE c.id
                    FROM c
                    WHERE c.docType = 'user'
                      AND c.handle = @handle
                      AND c.id != @userId
                      AND IS_NULL(c.deletedAt)
                    """)
                .WithParameter("@handle", handle)
                .WithParameter("@userId", userIdToExclude),
            cancellationToken);

    private Task<bool> EmailExistsAsync(string email, string userIdToExclude, CancellationToken cancellationToken) =>
        AnyUserAsync(
            new QueryDefinition(
                    """
                    SELECT VALUE c.id
                    FROM c
                    WHERE c.docType = 'user'
                      AND c.email = @email
                      AND c.id != @userId
                      AND IS_NULL(c.deletedAt)
                    """)
                .WithParameter("@email", email)
                .WithParameter("@userId", userIdToExclude),
            cancellationToken);

    private Task<bool> GoogleSubExistsAsync(string googleSub, string userIdToExclude, CancellationToken cancellationToken) =>
        AnyUserAsync(
            new QueryDefinition(
                    """
                    SELECT VALUE c.id
                    FROM c
                    WHERE c.docType = 'user'
                      AND c.auth.google.sub = @sub
                      AND c.id != @userId
                      AND IS_NULL(c.deletedAt)
                    """)
                .WithParameter("@sub", googleSub)
                .WithParameter("@userId", userIdToExclude),
            cancellationToken);

    private async Task<bool> AnyUserAsync(QueryDefinition query, CancellationToken cancellationToken)
    {
        using var iterator = _container.GetItemQueryIterator<string>(
            query,
            requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            if (page.Resource.Any())
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class DevicesRepository : IDevicesRepository
{
    private readonly Container _container;

    public DevicesRepository(CosmosClient client, CosmosOptions options)
    {
        _container = client.GetContainer(options.Database, options.DevicesContainer);
    }

    public async Task<DeviceDocument?> GetAsync(string userId, string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<DeviceDocument>(
                deviceId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DeviceDocument>> ListByUserAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId")
            .WithParameter("@userId", userId);
        using var iterator = _container.GetItemQueryIterator<DeviceDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        var results = new List<DeviceDocument>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page.Resource);
        }

        return results;
    }

    public Task UpsertAsync(DeviceDocument device, CancellationToken cancellationToken) =>
        _container.UpsertItemAsync(device, new PartitionKey(device.UserId), cancellationToken: cancellationToken);

    public Task UpdateAsync(DeviceDocument device, CancellationToken cancellationToken) =>
        _container.ReplaceItemAsync(device, device.Id, new PartitionKey(device.UserId), cancellationToken: cancellationToken);
}

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "mergeduo-identity",
            ConsistencyLevel = ConsistencyLevel.Session,
            ConnectionMode = ConnectionMode.Direct,
            EnableContentResponseOnWrite = false,
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        };

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new CosmosClient(options.ConnectionString, clientOptions);
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return new CosmosClient(options.Endpoint, new DefaultAzureCredential(), clientOptions);
        }

        throw new InvalidOperationException("Cosmos:Endpoint or Cosmos:ConnectionString must be configured.");
    }
}
