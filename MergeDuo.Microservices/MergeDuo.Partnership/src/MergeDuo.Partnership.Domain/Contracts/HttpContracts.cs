using System.Text.Json.Serialization;

namespace MergeDuo.Partnership.Domain.Contracts;

public sealed record CreateInviteRequest(
    [property: JsonPropertyName("channel")] string? Channel);

public sealed record CreateInviteResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("inviteUrl")] string InviteUrl,
    [property: JsonPropertyName("qrPayload")] string QrPayload,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);

public sealed record InvitePreviewResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("inviter")] PartnerSnapshotResponse Inviter,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);

public sealed record AcceptInviteResponse(
    [property: JsonPropertyName("partnershipId")] string PartnershipId,
    [property: JsonPropertyName("partnershipDocumentId")] string PartnershipDocumentId,
    [property: JsonPropertyName("status")] string Status);

public sealed record CurrentPartnershipResponse(
    [property: JsonPropertyName("partnership")] PartnershipResponse? Partnership);

public sealed record PartnershipResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("partnershipId")] string PartnershipId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("partnerUserId")] string PartnerUserId,
    [property: JsonPropertyName("partner")] PartnerSnapshotResponse Partner,
    [property: JsonPropertyName("startingBalance")] decimal StartingBalance,
    [property: JsonPropertyName("mergedSince")] DateOnly MergedSince,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("endedAt")] DateTimeOffset? EndedAt);

public sealed record PartnershipStatusResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("partnershipId")] string PartnershipId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("endedAt")] DateTimeOffset? EndedAt);

public sealed record PartnerSnapshotResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string Handle,
    [property: JsonPropertyName("initials")] string Initials);
