using System.Security.Cryptography;
using System.Text;
using MergeDuo.Identity.Domain.Abstractions;

namespace MergeDuo.Identity.Tests.Fakes;

public sealed class FakeGoogleIdTokenValidator : IGoogleIdTokenValidator
{
    public GooglePrincipal Principal { get; set; } = new(
        "google-sub-1",
        "gabriel.marques@gmail.com",
        true,
        "Gabriel Marques",
        "https://lh3.googleusercontent.com/a/test",
        null);
    public Exception? ExceptionToThrow { get; set; }

    public Task<GooglePrincipal> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        if (idToken == "invalid")
        {
            throw new InvalidOperationException("invalid token");
        }

        return Task.FromResult(Principal);
    }
}

public sealed class FakeAvatarStorage : IAvatarStorage
{
    public Dictionary<string, byte[]> Blobs { get; } = [];
    public List<string> DeletedUrls { get; } = [];

    public async Task<AvatarUploadResult> UploadAsync(
        string userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var ext = contentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "bin"
        };
        var url = $"https://stmergeduo.blob.core.windows.net/avatars/{userId}/{hash}.{ext}";
        Blobs[url] = bytes;
        return new AvatarUploadResult(url, $"{userId}/{hash}.{ext}", hash);
    }

    public Task DeleteByUrlAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            DeletedUrls.Add(avatarUrl);
            Blobs.Remove(avatarUrl);
        }

        return Task.CompletedTask;
    }
}

public sealed class StaticHttpMessageHandler(string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        });
    }
}
