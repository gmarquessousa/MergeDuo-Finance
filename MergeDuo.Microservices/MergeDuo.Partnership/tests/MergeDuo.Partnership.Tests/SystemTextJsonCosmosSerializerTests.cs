using System.Text;
using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Infra.Cosmos;

namespace MergeDuo.Partnership.Tests;

public sealed class SystemTextJsonCosmosSerializerTests
{
    [Fact]
    public void Deserializes_etag_from_underscore_etag_field()
    {
        const string json = """
            {
                "id":"invite_x",
                "docType":"mergeInvite",
                "schemaVersion":1,
                "token":"abc",
                "inviterUserId":"usr_g",
                "channel":"link",
                "status":"pending",
                "inviterSnapshot":{"userId":"usr_g","name":"G","handle":"@g","initials":"G"},
                "createdAt":"2026-04-30T03:00:00Z",
                "updatedAt":"2026-04-30T03:00:00Z",
                "expiresAt":"2026-05-03T03:00:00Z",
                "_etag":"\"abc-123\""
            }
            """;

        var serializer = new SystemTextJsonCosmosSerializer();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var doc = serializer.FromStream<MergeInviteDocument>(stream);

        Assert.Equal("invite_x", doc.Id);
        Assert.Equal("\"abc-123\"", doc.ETag);
    }

    [Fact]
    public void Serializes_writing_underscore_etag_and_skipping_null_ttl()
    {
        var doc = new MergeInviteDocument
        {
            Id = "invite_x",
            Token = "abc",
            InviterUserId = "usr_g",
            Channel = "link",
            ETag = "\"v1\""
        };

        var serializer = new SystemTextJsonCosmosSerializer();
        using var stream = serializer.ToStream(doc);
        var json = new StreamReader(stream).ReadToEnd();

        Assert.Contains("\"_etag\":", json);
        Assert.DoesNotContain("\"ttl\":", json);
    }
}
