using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Options;

namespace MergeDuo.Identity.Infra.Storage;

public sealed class BlobAvatarStorage(BlobStorageOptions options) : IAvatarStorage
{
    private const int MaxAvatarBytes = 2 * 1024 * 1024;
    private readonly BlobContainerClient _container = CreateContainer(options);

    public async Task<AvatarUploadResult> UploadAsync(
        string userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxAvatarBytes)
        {
            throw new InvalidOperationException("avatar_too_large");
        }

        var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        var ext = contentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => throw new InvalidOperationException("unsupported_avatar_type")
        };

        var blobName = $"{userId}/{hash}.{ext}";
        var blob = _container.GetBlobClient(blobName);
        buffer.Position = 0;
        await blob.UploadAsync(
            buffer,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);

        return new AvatarUploadResult(blob.Uri.ToString(), blobName, hash);
    }

    public async Task DeleteByUrlAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl) || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var prefix = "/" + options.AvatarsContainer + "/";
        var path = uri.AbsolutePath;
        var index = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return;
        }

        var blobName = Uri.UnescapeDataString(path[(index + prefix.Length)..]);
        await _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private static BlobContainerClient CreateContainer(BlobStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobContainerClient(options.ConnectionString, options.AvatarsContainer);
        }

        if (!string.IsNullOrWhiteSpace(options.AccountUrl))
        {
            return new BlobContainerClient(new Uri(new Uri(options.AccountUrl), options.AvatarsContainer));
        }

        throw new InvalidOperationException("BlobStorage configuration is missing. Provide ConnectionString or AccountUrl.");
    }
}
