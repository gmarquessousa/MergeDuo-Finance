using MergeDuo.Identity.Domain.Documents;

namespace MergeDuo.Identity.Domain.Abstractions;

public interface IUsersRepository
{
    Task<UserDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken);
    Task<UserDocument?> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserDocument>> ListAllAsync(CancellationToken cancellationToken);
    Task CreateAsync(UserDocument user, CancellationToken cancellationToken);
    Task UpdateLoginSnapshotAsync(
        string userId,
        string googleEmail,
        bool googleEmailVerified,
        string? googlePictureUrl,
        string? googleHostedDomain,
        DateTimeOffset lastLoginAt,
        CancellationToken cancellationToken);
    Task UpdateAsync(UserDocument user, string? ifMatchEtag, CancellationToken cancellationToken);
}

public interface IDevicesRepository
{
    Task<DeviceDocument?> GetAsync(string userId, string deviceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceDocument>> ListByUserAsync(string userId, CancellationToken cancellationToken);
    Task UpsertAsync(DeviceDocument device, CancellationToken cancellationToken);
    Task UpdateAsync(DeviceDocument device, CancellationToken cancellationToken);
}

public sealed class IdentityConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class IdentityDependencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
