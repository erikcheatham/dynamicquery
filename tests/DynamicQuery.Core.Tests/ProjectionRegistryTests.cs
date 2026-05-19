using DynamicQuery.Core;
using Xunit;

namespace DynamicQuery.Core.Tests;

public class ProjectionRegistryTests
{
    // ─── Fixture DTOs ──────────────────────────────────────────────

    [Projection("widgets", "w")]
    private class SimpleDto
    {
        [Column("w.id")]
        public Guid Id { get; set; }

        [Column("w.name")]
        public string Name { get; set; } = string.Empty;
    }

    [Projection("reviews", "r")]
    [LeftJoin("media", "m", "r.media_id = m.id")]
    [LeftJoin("users", "u", "u.id = r.created_by")]
    private class JoinedDto
    {
        [Column("r.id")]
        public Guid Id { get; set; }

        [Coalesce("m.title", "r.standalone_title")]
        public string? Title { get; set; }

        [JsonbPath("r.content_json", 0, "platform")]
        public string? Platform { get; set; }

        [Column("u.user_name")]
        public string? AuthorHandle { get; set; }
    }

    [Projection("things", "t")]
    private class TransientPropDto
    {
        [Column("t.id")]
        public Guid Id { get; set; }

        // No DynamicQuery attribute — should be skipped.
        public string TransientField { get; set; } = string.Empty;
    }

    [Projection("conflicts", "c")]
    private class DoubleAttributeDto
    {
        [Column("c.foo")]
        [Coalesce("c.foo", "c.bar")]
        public string? Field { get; set; }
    }

    private class NoProjectionDto
    {
        [Column("x.foo")]
        public string? Foo { get; set; }
    }

    [Projection("empty", "e")]
    private class NoColumnsDto
    {
        public string? Foo { get; set; }
    }

    // ─── SELECT projection emission ────────────────────────────────

    [Fact]
    public void GetSelectColumns_SimpleDto_EmitsColumnsWithAlias()
    {
        ProjectionRegistry.ClearCache();
        var sql = ProjectionRegistry.GetSelectColumns<SimpleDto>();
        Assert.Contains("w.id AS \"Id\"", sql);
        Assert.Contains("w.name AS \"Name\"", sql);
    }

    [Fact]
    public void GetSelectColumns_JoinedDto_EmitsCoalesceAndJsonbPath()
    {
        ProjectionRegistry.ClearCache();
        var sql = ProjectionRegistry.GetSelectColumns<JoinedDto>();

        Assert.Contains("r.id AS \"Id\"", sql);
        Assert.Contains("COALESCE(m.title, r.standalone_title) AS \"Title\"", sql);
        Assert.Contains("(r.content_json::jsonb -> 0 ->> 'platform') AS \"Platform\"", sql);
        Assert.Contains("u.user_name AS \"AuthorHandle\"", sql);
    }

    [Fact]
    public void GetSelectColumns_TransientPropDto_SkipsUnannotatedProperties()
    {
        ProjectionRegistry.ClearCache();
        var sql = ProjectionRegistry.GetSelectColumns<TransientPropDto>();
        Assert.Contains("t.id AS \"Id\"", sql);
        Assert.DoesNotContain("TransientField", sql);
    }

    // ─── FROM block emission ───────────────────────────────────────

    [Fact]
    public void GetFromClause_SimpleDto_EmitsTableAndAlias()
    {
        ProjectionRegistry.ClearCache();
        var sql = ProjectionRegistry.GetFromClause<SimpleDto>();
        Assert.Equal("widgets w", sql);
    }

    [Fact]
    public void GetFromClause_JoinedDto_EmitsLeftJoinsInDeclarationOrder()
    {
        ProjectionRegistry.ClearCache();
        var sql = ProjectionRegistry.GetFromClause<JoinedDto>();

        Assert.StartsWith("reviews r", sql);
        Assert.Contains("LEFT JOIN media m ON r.media_id = m.id", sql);
        Assert.Contains("LEFT JOIN users u ON u.id = r.created_by", sql);

        // Declaration order: media before users.
        var mediaPos = sql.IndexOf("LEFT JOIN media m", StringComparison.Ordinal);
        var usersPos = sql.IndexOf("LEFT JOIN users u", StringComparison.Ordinal);
        Assert.True(mediaPos < usersPos, "Joins should emit in declaration order.");
    }

    // ─── Validation errors ─────────────────────────────────────────

    [Fact]
    public void GetDescriptor_MissingProjectionAttribute_Throws()
    {
        ProjectionRegistry.ClearCache();
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProjectionRegistry.GetDescriptor<NoProjectionDto>());
        Assert.Contains("[Projection]", ex.Message);
    }

    [Fact]
    public void GetDescriptor_NoAnnotatedColumns_Throws()
    {
        ProjectionRegistry.ClearCache();
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProjectionRegistry.GetDescriptor<NoColumnsDto>());
        Assert.Contains("no DynamicQuery column attributes", ex.Message);
    }

    [Fact]
    public void GetDescriptor_MultipleAttributesOnOneProperty_Throws()
    {
        ProjectionRegistry.ClearCache();
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProjectionRegistry.GetDescriptor<DoubleAttributeDto>());
        Assert.Contains("multiple", ex.Message);
    }

    // ─── Caching behavior ──────────────────────────────────────────

    [Fact]
    public void GetDescriptor_SameTypeTwice_ReturnsSameInstance()
    {
        ProjectionRegistry.ClearCache();
        var first = ProjectionRegistry.GetDescriptor<SimpleDto>();
        var second = ProjectionRegistry.GetDescriptor<SimpleDto>();
        Assert.Same(first, second);
    }

    [Fact]
    public void GetDescriptor_DifferentTypes_ReturnsDifferentInstances()
    {
        ProjectionRegistry.ClearCache();
        var a = ProjectionRegistry.GetDescriptor<SimpleDto>();
        var b = ProjectionRegistry.GetDescriptor<JoinedDto>();
        Assert.NotSame(a, b);
    }
}
