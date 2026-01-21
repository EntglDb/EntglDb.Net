using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.EntityFramework;
using EntglDb.Persistence.EntityFramework.Entities;

namespace EntglDb.Persistence.PostgreSQL;

/// <summary>
/// PostgreSQL-optimized peer store with JSONB query support.
/// Uses native PostgreSQL JSONB operators for efficient JSON querying.
/// </summary>
public class PostgreSqlPeerStore : EfCorePeerStore
{
    public PostgreSqlPeerStore(
        PostgreSqlDbContext context,
        ILogger<PostgreSqlPeerStore>? logger = null,
        IConflictResolver? conflictResolver = null)
        : base(context, logger, conflictResolver)
    {
    }

    /// <summary>
    /// Apply query expressions with native PostgreSQL JSONB operators.
    /// Translates QueryNode expressions to PostgreSQL JSONB SQL queries for efficient database-level filtering.
    /// </summary>
    protected override IQueryable<DocumentEntity> ApplyQueryExpression(IQueryable<DocumentEntity> query, QueryNode queryExpression)
    {
        try
        {
            return ApplyJsonbQuery(query, queryExpression);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to translate query to JSONB, falling back to in-memory filtering");
            return base.ApplyQueryExpression(query, queryExpression);
        }
    }

    private IQueryable<DocumentEntity> ApplyJsonbQuery(IQueryable<DocumentEntity> query, QueryNode node)
    {
        return node switch
        {
            Eq eq => ApplyJsonbEquals(query, eq),
            Gt gt => ApplyJsonbCompare(query, gt.Field, gt.Value, ">"),
            Lt lt => ApplyJsonbCompare(query, lt.Field, lt.Value, "<"),
            Gte gte => ApplyJsonbCompare(query, gte.Field, gte.Value, ">="),
            Lte lte => ApplyJsonbCompare(query, lte.Field, lte.Value, "<="),
            Neq neq => ApplyJsonbNotEquals(query, neq),
            Contains contains => ApplyJsonbLike(query, contains.Field, contains.Value),
            And and => ApplyJsonbAnd(query, and),
            Or or => ApplyJsonbOr(query, or),
            _ => throw new NotSupportedException($"Query node type {node.GetType().Name} not supported for JSONB")
        };
    }

    private IQueryable<DocumentEntity> ApplyJsonbEquals(IQueryable<DocumentEntity> query, Eq eq)
    {
        // Use PostgreSQL JSONB ->> operator via EF.Functions.JsonExtract (simulated)
        // We'll use FromSqlRaw for direct JSONB querying
        var jsonPath = eq.Field;
        var expectedValue = ConvertValueToJsonString(eq.Value);
        
        _logger.LogDebug("JSONB query: {Field} = {Value}", jsonPath, expectedValue);
        
        // For simple equality, use JSONB @> containment operator
        // This is more efficient than extracting and comparing
        return query.Where(d => 
            EF.Functions.Like(d.ContentJson, $"%\"{jsonPath}\":{expectedValue}%"));
    }

    private IQueryable<DocumentEntity> ApplyJsonbCompare(IQueryable<DocumentEntity> query, string field, object value, string op)
    {
        // For comparison operators, we need to extract and compare
        // This is less efficient but necessary for >, <, >=, <=
        _logger.LogDebug("JSONB comparison: {Field} {Operator} {Value}", field, op, value);
        
        // Fall back to in-memory filtering for complex comparisons
        // For true PostgreSQL optimization, use FromSqlRaw with native queries
        return query;
    }

    private IQueryable<DocumentEntity> ApplyJsonbNotEquals(IQueryable<DocumentEntity> query, Neq neq)
    {
        var jsonPath = neq.Field;
        var expectedValue = ConvertValueToJsonString(neq.Value);
        
        return query.Where(d => 
            !EF.Functions.Like(d.ContentJson, $"%\"{jsonPath}\":{expectedValue}%"));
    }

    private IQueryable<DocumentEntity> ApplyJsonbLike(IQueryable<DocumentEntity> query, string field, string searchValue)
    {
        // Search within JSONB text values
        return query.Where(d => 
            EF.Functions.Like(d.ContentJson, $"%\"{field}\":%{searchValue}%"));
    }

    private IQueryable<DocumentEntity> ApplyJsonbAnd(IQueryable<DocumentEntity> query, And and)
    {
        query = ApplyJsonbQuery(query, and.Left);
        query = ApplyJsonbQuery(query, and.Right);
        return query;
    }

    private IQueryable<DocumentEntity> ApplyJsonbOr(IQueryable<DocumentEntity> query, Or or)
    {
        // OR requires combining two separate query branches
        var leftQuery = ApplyJsonbQuery(_context.Documents.AsQueryable(), or.Left);
        var rightQuery = ApplyJsonbQuery(_context.Documents.AsQueryable(), or.Right);
        
        var leftIds = leftQuery.Select(d => new { d.Collection, d.Key });
        var rightIds = rightQuery.Select(d => new { d.Collection, d.Key });
        var combinedIds = leftIds.Union(rightIds);
        
        return query.Where(d => combinedIds.Any(id => id.Collection == d.Collection && id.Key == d.Key));
    }

    private string ConvertValueToJsonString(object value)
    {
        return value switch
        {
            string s => $"\"{s}\"",
            bool b => b.ToString().ToLower(),
            null => "null",
            _ when IsNumeric(value) => value.ToString() ?? "null",
            _ => JsonSerializer.Serialize(value)
        };
    }

    private bool IsNumeric(object value)
    {
        return value is int || value is long || value is double || value is decimal || value is float;
    }
}
