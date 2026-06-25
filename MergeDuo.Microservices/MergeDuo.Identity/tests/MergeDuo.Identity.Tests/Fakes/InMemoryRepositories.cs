using System.Collections.Concurrent;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Documents;
using MergeDuo.Identity.Domain.Rules;

namespace MergeDuo.Identity.Tests.Fakes;

public sealed class InMemoryUsersRepository : IUsersRepository
{
    private readonly ConcurrentDictionary<string, UserDocument> _users = new();
    private readonly ConcurrentDictionary<string, IdentityReservationDocument> _reservations = new();

    public IReadOnlyCollection<UserDocument> Users => _users.Values.ToArray();
    public IReadOnlyCollection<IdentityReservationDocument> Reservations => _reservations.Values.ToArray();
    public int GenericUpdateCalls { get; private set; }
    public int LoginSnapshotUpdateCalls { get; private set; }

    public Task<UserDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(Clone(user));
    }

    public Task<UserDocument?> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken)
    {
        return Task.FromResult(Clone(_users.Values.FirstOrDefault(x => x.Auth.Google.Sub == googleSub)));
    }

    public Task<IReadOnlyList<UserDocument>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserDocument>>(_users.Values.Select(Clone).OfType<UserDocument>().ToArray());

    public Task CreateAsync(UserDocument user, CancellationToken cancellationToken)
    {
        if (_users.Values.Any(x =>
            x.Handle == user.Handle ||
            x.Email == user.Email ||
            x.Auth.Google.Sub == user.Auth.Google.Sub))
        {
            throw new IdentityConflictException("unique_key_conflict", "conflict");
        }

        var pending = ReserveAll(user);
        user.ETag = Guid.NewGuid().ToString("N");
        _users[user.Id] = Clone(user)!;
        ActivateAll(pending, user.Id);
        return Task.CompletedTask;
    }

    public Task UpdateLoginSnapshotAsync(
        string userId,
        string googleEmail,
        bool googleEmailVerified,
        string? googlePictureUrl,
        string? googleHostedDomain,
        DateTimeOffset lastLoginAt,
        CancellationToken cancellationToken)
    {
        LoginSnapshotUpdateCalls++;
        var user = _users.GetValueOrDefault(userId)
            ?? throw new InvalidOperationException("User not found.");
        user.Auth.Google.Email = googleEmail;
        user.Auth.Google.EmailVerified = googleEmailVerified;
        user.Auth.Google.PictureUrl = googlePictureUrl;
        user.Auth.Google.HostedDomain = googleHostedDomain;
        user.Auth.LastLoginAt = lastLoginAt;
        user.UpdatedAt = lastLoginAt;
        user.ETag = Guid.NewGuid().ToString("N");
        _users[userId] = Clone(user)!;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserDocument user, string? ifMatchEtag, CancellationToken cancellationToken)
    {
        GenericUpdateCalls++;
        if (_users.Values.Any(x => x.Id != user.Id && x.Handle == user.Handle))
        {
            throw new IdentityConflictException("handle_already_taken", "handle conflict");
        }

        if (_users.Values.Any(x => x.Id != user.Id && (x.Email == user.Email || x.Auth.Google.Sub == user.Auth.Google.Sub)))
        {
            throw new IdentityConflictException("unique_key_conflict", "conflict");
        }

        var previous = _users.GetValueOrDefault(user.Id);
        var pending = ReserveAll(user);
        user.ETag = Guid.NewGuid().ToString("N");
        _users[user.Id] = Clone(user)!;
        ActivateAll(pending, user.Id);
        if (previous is not null)
        {
            ReleaseOld(previous, user, user.Id);
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<IdentityReservationValue> ReserveAll(UserDocument user)
    {
        var values = IdentityReservationRules.ForUser(user);
        foreach (var value in values)
        {
            if (_reservations.TryGetValue(value.Id, out var existing))
            {
                if (existing.UserId == user.Id && existing.Status == IdentityReservationRules.StatusActive)
                {
                    continue;
                }

                throw new IdentityConflictException(
                    value.Kind == IdentityReservationRules.KindHandle ? "handle_already_taken" : "unique_key_conflict",
                    "reservation conflict");
            }

            _reservations[value.Id] = IdentityReservationRules.ToDocument(
                value,
                user.Id,
                IdentityReservationRules.StatusPending,
                DateTimeOffset.UtcNow);
        }

        return values;
    }

    private void ActivateAll(IEnumerable<IdentityReservationValue> values, string userId)
    {
        foreach (var value in values)
        {
            if (!_reservations.TryGetValue(value.Id, out var reservation) || reservation.UserId != userId)
            {
                continue;
            }

            reservation.Status = IdentityReservationRules.StatusActive;
            reservation.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private void ReleaseOld(UserDocument previous, UserDocument current, string userId)
    {
        var currentIds = IdentityReservationRules.ForUser(current).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var value in IdentityReservationRules.ForUser(previous).Where(x => !currentIds.Contains(x.Id)))
        {
            if (!_reservations.TryGetValue(value.Id, out var reservation) || reservation.UserId != userId)
            {
                continue;
            }

            reservation.Status = IdentityReservationRules.StatusReleased;
            reservation.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static UserDocument? Clone(UserDocument? user)
    {
        if (user is null)
        {
            return null;
        }

        return new UserDocument
        {
            Id = user.Id,
            DocType = user.DocType,
            SchemaVersion = user.SchemaVersion,
            Name = user.Name,
            Handle = user.Handle,
            Email = user.Email,
            Phone = user.Phone,
            AvatarInitials = user.AvatarInitials,
            AvatarUrl = user.AvatarUrl,
            MemberSince = user.MemberSince,
            RegisteredAt = user.RegisteredAt,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            DeletedAt = user.DeletedAt,
            ETag = user.ETag,
            Financial = new UserFinancial
            {
                StartingBalance = user.Financial.StartingBalance,
                Currency = user.Financial.Currency
            },
            Preferences = new UserPreferences
            {
                DarkMode = user.Preferences.DarkMode,
                WeeklyReport = user.Preferences.WeeklyReport
            },
            Stats = new UserStats
            {
                TransactionsTracked = user.Stats.TransactionsTracked,
                ActiveMonths = user.Stats.ActiveMonths,
                LastRecomputedAt = user.Stats.LastRecomputedAt
            },
            Auth = new UserAuth
            {
                LastLoginAt = user.Auth.LastLoginAt,
                Google = new GoogleAuthState
                {
                    Sub = user.Auth.Google.Sub,
                    Email = user.Auth.Google.Email,
                    EmailVerified = user.Auth.Google.EmailVerified,
                    HostedDomain = user.Auth.Google.HostedDomain,
                    PictureUrl = user.Auth.Google.PictureUrl,
                    LinkedAt = user.Auth.Google.LinkedAt
                }
            }
        };
    }
}

public sealed class InMemoryDevicesRepository : IDevicesRepository
{
    private readonly ConcurrentDictionary<(string UserId, string DeviceId), DeviceDocument> _devices = new();

    public IReadOnlyCollection<DeviceDocument> Devices => _devices.Values.ToArray();

    public Task<DeviceDocument?> GetAsync(string userId, string deviceId, CancellationToken cancellationToken)
    {
        _devices.TryGetValue((userId, deviceId), out var device);
        return Task.FromResult(device);
    }

    public Task<IReadOnlyList<DeviceDocument>> ListByUserAsync(string userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<DeviceDocument> result = _devices.Values.Where(x => x.UserId == userId).ToArray();
        return Task.FromResult(result);
    }

    public Task UpsertAsync(DeviceDocument device, CancellationToken cancellationToken)
    {
        _devices[(device.UserId, device.Id)] = device;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DeviceDocument device, CancellationToken cancellationToken)
    {
        _devices[(device.UserId, device.Id)] = device;
        return Task.CompletedTask;
    }
}
