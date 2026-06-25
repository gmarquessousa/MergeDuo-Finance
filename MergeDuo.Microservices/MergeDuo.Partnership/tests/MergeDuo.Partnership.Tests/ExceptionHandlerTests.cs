using System.Net;
using System.Net.Http.Json;
using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Domain.Contracts;
using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MergeDuo.Partnership.Tests;

public sealed class ExceptionHandlerTests
{
    [Fact]
    public async Task Throttled_repository_returns_429_with_retry_after()
    {
        using var baseFactory = new TestPartnershipFactory();
        var requester = new UserSummaryDocument
        {
            Id = "usr_throttled",
            Handle = "@throttled",
            ETag = Guid.NewGuid().ToString("N")
        };
        baseFactory.Users.Add(requester);

        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInvitesRepository>();
                services.AddSingleton<IInvitesRepository>(new ThrowingInvitesRepository(
                    new PartnershipThrottledException(
                        "partnership_throttled",
                        "Failed to create invite.",
                        TimeSpan.FromSeconds(5))));
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/invites")
        {
            Content = JsonContent.Create(new CreateInviteRequest("link"))
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            baseFactory.IssueToken(requester.Id));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var values));
        Assert.Equal("5", Assert.Single(values));
    }

    [Fact]
    public async Task Bad_request_from_repository_returns_400()
    {
        using var baseFactory = new TestPartnershipFactory();
        var requester = new UserSummaryDocument
        {
            Id = "usr_badreq",
            Handle = "@badreq",
            ETag = Guid.NewGuid().ToString("N")
        };
        baseFactory.Users.Add(requester);

        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInvitesRepository>();
                services.AddSingleton<IInvitesRepository>(new ThrowingInvitesRepository(
                    new PartnershipBadRequestException("invite_payload_invalid", "Failed to create invite.")));
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/invites")
        {
            Content = JsonContent.Create(new CreateInviteRequest("link"))
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            baseFactory.IssueToken(requester.Id));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class ThrowingInvitesRepository : IInvitesRepository
    {
        private readonly Exception _exception;

        public ThrowingInvitesRepository(Exception exception)
        {
            _exception = exception;
        }

        public Task<MergeInviteDocument?> GetPendingForInviterAsync(string inviterUserId, CancellationToken cancellationToken) =>
            Task.FromResult<MergeInviteDocument?>(null);

        public Task<IReadOnlyList<MergeInviteDocument>> FindByTokenAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MergeInviteDocument>>(Array.Empty<MergeInviteDocument>());

        public Task CreateAsync(MergeInviteDocument invite, CancellationToken cancellationToken) =>
            throw _exception;

        public Task MarkAcceptedAsync(MergeInviteDocument invite, AcceptedBySnapshot acceptedBy, string partnershipId, DateTimeOffset acceptedAt, string ifMatchEtag, CancellationToken cancellationToken) =>
            throw _exception;

        public Task MarkRevokedAsync(MergeInviteDocument invite, DateTimeOffset revokedAt, string ifMatchEtag, CancellationToken cancellationToken) =>
            throw _exception;

        public Task MarkExpiredAsync(MergeInviteDocument invite, DateTimeOffset expiredAt, string ifMatchEtag, CancellationToken cancellationToken) =>
            throw _exception;
    }
}
