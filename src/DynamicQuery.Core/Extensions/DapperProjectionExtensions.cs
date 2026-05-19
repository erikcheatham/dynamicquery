using System.Data;
using Dapper;

namespace DynamicQuery.Core;

/// <summary>
/// Optional convenience extensions composing
/// <see cref="ProjectionRegistry"/>'s SQL fragments into common Dapper
/// query shapes. Consumers needing tighter control over the query
/// (CTEs, UNIONs, complex WHERE composition) should compose by hand
/// using <see cref="ProjectionRegistry.GetSelectColumns{T}"/> and
/// <see cref="ProjectionRegistry.GetFromClause{T}"/> directly.
/// </summary>
public static class DapperProjectionExtensions
{
    /// <summary>
    /// Composes <c>SELECT {projection} FROM {fromClause} [WHERE ...]
    /// [ORDER BY ...] [LIMIT ...]</c> and dispatches via Dapper. Each
    /// clause is optional and omitted when null/empty.
    /// </summary>
    /// <typeparam name="T">DTO type with a [Projection] attribute.</typeparam>
    /// <param name="connection">Open or closed IDbConnection. Dapper
    /// handles opening if needed.</param>
    /// <param name="where">Optional WHERE clause body (without the
    /// <c>WHERE</c> keyword). Reference parameters with <c>@Name</c>
    /// syntax and pass values via <paramref name="parameters"/>.</param>
    /// <param name="parameters">Dapper parameter object (anonymous type
    /// or DynamicParameters).</param>
    /// <param name="orderBy">Optional ORDER BY clause body (without the
    /// <c>ORDER BY</c> keyword).</param>
    /// <param name="limit">Optional row limit appended as
    /// <c>LIMIT &lt;n&gt;</c>. Postgres-compatible; SQL Server callers
    /// should use <paramref name="where"/> + custom SQL instead.</param>
    /// <param name="commandTimeout">Optional Dapper command timeout.</param>
    public static Task<IEnumerable<T>> QueryProjectionAsync<T>(
        this IDbConnection connection,
        string? where = null,
        object? parameters = null,
        string? orderBy = null,
        int? limit = null,
        int? commandTimeout = null)
    {
        var sql = BuildSql<T>(where, orderBy, limit);
        return connection.QueryAsync<T>(sql, parameters, commandTimeout: commandTimeout);
    }

    /// <summary>
    /// Single-row variant. Returns <c>default(T)</c> when no row matches.
    /// </summary>
    public static Task<T?> QueryProjectionSingleOrDefaultAsync<T>(
        this IDbConnection connection,
        string? where = null,
        object? parameters = null,
        int? commandTimeout = null)
    {
        var sql = BuildSql<T>(where, orderBy: null, limit: null);
        return connection.QuerySingleOrDefaultAsync<T>(sql, parameters, commandTimeout: commandTimeout);
    }

    /// <summary>
    /// Single-row variant that throws when no row matches.
    /// </summary>
    public static Task<T> QueryProjectionSingleAsync<T>(
        this IDbConnection connection,
        string? where = null,
        object? parameters = null,
        int? commandTimeout = null)
    {
        var sql = BuildSql<T>(where, orderBy: null, limit: null);
        return connection.QuerySingleAsync<T>(sql, parameters, commandTimeout: commandTimeout);
    }

    private static string BuildSql<T>(string? where, string? orderBy, int? limit)
    {
        var descriptor = ProjectionRegistry.GetDescriptor<T>();
        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT ").Append(descriptor.SelectColumns)
          .Append('\n')
          .Append("FROM ").Append(descriptor.FromClause);

        if (!string.IsNullOrWhiteSpace(where))
        {
            sb.Append('\n').Append("WHERE ").Append(where);
        }

        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            sb.Append('\n').Append("ORDER BY ").Append(orderBy);
        }

        if (limit is int n && n > 0)
        {
            sb.Append('\n').Append("LIMIT ").Append(n);
        }

        return sb.ToString();
    }
}
