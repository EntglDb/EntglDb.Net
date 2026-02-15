using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using EntglDb.Core;

namespace EntglDb.Persistence.Blite.Internal;

internal static class QueryTranslator
{
    private static readonly MethodInfo GetPropertyValueMethod = typeof(QueryTranslator).GetMethod(nameof(GetPropertyValue), BindingFlags.Static | BindingFlags.NonPublic)!;

    public static Expression<Func<Document, bool>> Translate(string collection, QueryNode? query)
    {
        var parameter = Expression.Parameter(typeof(Document), "doc");
        
        // Redundant check removed: DocumentCollection already filters by collection
        // Expression expr = Expression.Equal(
        //     Expression.Property(parameter, nameof(Document.Collection)),
        //     Expression.Constant(collection)
        // );

        if (query == null)
        {
            return doc => true;
        }

        return Expression.Lambda<Func<Document, bool>>(TranslateNode(query, parameter), parameter);
    }

    private static Expression TranslateNode(QueryNode node, ParameterExpression parameter)
    {
        return node switch
        {
            Eq eq => Expression.Equal(GetFieldExpression(parameter, eq.Field, eq.Value?.GetType() ?? typeof(object)), Expression.Constant(eq.Value)),
            Gt gt => Expression.GreaterThan(GetFieldExpression(parameter, gt.Field, gt.Value.GetType()), Expression.Constant(gt.Value)),
            Lt lt => Expression.LessThan(GetFieldExpression(parameter, lt.Field, lt.Value.GetType()), Expression.Constant(lt.Value)),
            Gte gte => Expression.GreaterThanOrEqual(GetFieldExpression(parameter, gte.Field, gte.Value.GetType()), Expression.Constant(gte.Value)),
            Lte lte => Expression.LessThanOrEqual(GetFieldExpression(parameter, lte.Field, lte.Value.GetType()), Expression.Constant(lte.Value)),
            Neq neq => Expression.NotEqual(GetFieldExpression(parameter, neq.Field, neq.Value?.GetType() ?? typeof(object)), Expression.Constant(neq.Value)),
            And and => Expression.AndAlso(TranslateNode(and.Left, parameter), TranslateNode(and.Right, parameter)),
            Or or => Expression.OrElse(TranslateNode(or.Left, parameter), TranslateNode(or.Right, parameter)),
            _ => throw new NotSupportedException($"Query node type {node.GetType().Name} is not supported.")
        };
    }

    private static Expression GetFieldExpression(ParameterExpression parameter, string field, Type targetType)
    {
        if (field.Equals("key", StringComparison.OrdinalIgnoreCase))
        {
            return Expression.Property(parameter, nameof(Document.Key));
        }

        if (field.Equals("updatedat", StringComparison.OrdinalIgnoreCase) || field.Equals("timestamp", StringComparison.OrdinalIgnoreCase))
        {
            return Expression.Property(parameter, nameof(Document.UpdatedAt));
        }

        if (field.Equals("isdeleted", StringComparison.OrdinalIgnoreCase))
        {
            return Expression.Property(parameter, nameof(Document.IsDeleted));
        }

        var contentProp = Expression.Property(parameter, nameof(Document.Content));
        var fieldConst = Expression.Constant(field);
        var typeConst = Expression.Constant(targetType);

        var call = Expression.Call(GetPropertyValueMethod, contentProp, fieldConst, typeConst);
        return Expression.Convert(call, targetType);
    }

    private static object? GetPropertyValue(JsonElement element, string path, Type targetType)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        // Force lowercase to match dictionary policy in Mappers.cs
        var lowercasePath = path.ToLowerInvariant();

        if (element.TryGetProperty(lowercasePath, out var prop))
        {
            object? val = null;
            if (targetType == typeof(string)) val = prop.GetString();
            else if (targetType == typeof(int)) val = prop.GetInt32();
            else if (targetType == typeof(long)) val = prop.GetInt64();
            else if (targetType == typeof(double)) val = prop.GetDouble();
            else if (targetType == typeof(bool)) val = prop.GetBoolean();
            else if (targetType == typeof(DateTime)) val = prop.GetDateTime();
            else if (targetType == typeof(Guid)) val = prop.GetGuid();

            return val;
        }

        return null;
    }
}
