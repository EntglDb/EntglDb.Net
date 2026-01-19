using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntglDb.Core;

/// <summary>
/// Translates LINQ Expression trees to <see cref="QueryNode"/> for database querying.
/// </summary>
public static class ExpressionToQueryNodeTranslator
{
    /// <summary>
    /// Translates a LINQ predicate expression to a QueryNode.
    /// </summary>
    /// <param name="predicate">The predicate to translate.</param>
    /// <param name="options">Optional JSON serialization options to respect naming policies and converters.</param>
    /// <returns>A QueryNode representing the query, or null if the predicate is always true.</returns>
    public static QueryNode Translate<T>(Expression<Func<T, bool>> predicate, JsonSerializerOptions? options = null)
    {
        if (predicate == null) return null;
        return Visit(predicate.Body, options);
    }

    private static QueryNode Visit(Expression node, JsonSerializerOptions? options)
    {
        switch (node.NodeType)
        {
            case ExpressionType.Constant:
                var c = (ConstantExpression)node;
                if (c.Value is bool b && b) return null; // "true" => All
                // "false" => not supported yet (could be 1=0 query node)
                 throw new NotSupportedException($"Constant expression '{c.Value}' not supported as full predicate (except 'true').");

            case ExpressionType.AndAlso:
                var and = (BinaryExpression)node;
                return new And(Visit(and.Left, options), Visit(and.Right, options));

            case ExpressionType.OrElse:
                var or = (BinaryExpression)node;
                return new Or(Visit(or.Left, options), Visit(or.Right, options));

            case ExpressionType.Equal:
                var eq = (BinaryExpression)node;
                return new Eq(GetFieldName(eq.Left, options), GetValue(eq.Right, options));

            case ExpressionType.GreaterThan:
                var gt = (BinaryExpression)node;
                return new Gt(GetFieldName(gt.Left, options), GetValue(gt.Right, options));

            case ExpressionType.LessThan:
                var lt = (BinaryExpression)node;
                return new Lt(GetFieldName(lt.Left, options), GetValue(lt.Right, options));

            case ExpressionType.GreaterThanOrEqual:
                var gte = (BinaryExpression)node;
                return new Gte(GetFieldName(gte.Left, options), GetValue(gte.Right, options));

            case ExpressionType.LessThanOrEqual:
                var lte = (BinaryExpression)node;
                return new Lte(GetFieldName(lte.Left, options), GetValue(lte.Right, options));

            case ExpressionType.NotEqual:
                var neq = (BinaryExpression)node;
                return new Neq(GetFieldName(neq.Left, options), GetValue(neq.Right, options));

            case ExpressionType.Call:
                 var call = (MethodCallExpression)node;
                 if (call.Method.Name == "Contains")
                 {
                     if (call.Object != null && call.Object.Type == typeof(string))
                     {
                         return new Contains(GetFieldName(call.Object, options), (string)GetValue(call.Arguments[0], options));
                     }
                 }
                 break;
        }

        throw new NotSupportedException($"Expression type {node.NodeType} is not supported");
    }

    private static string GetFieldName(Expression node, JsonSerializerOptions? options)
    {
        if (node.NodeType == ExpressionType.Convert)
        {
            return GetFieldName(((UnaryExpression)node).Operand, options);
        }

        if (node is MemberExpression member)
        {
            var name = member.Member.Name;
            var parent = member.Expression;

            // Apply Naming Policy if present
            if (options?.PropertyNamingPolicy != null)
            {
                name = options.PropertyNamingPolicy.ConvertName(name);
            }

            if (parent != null && (parent.NodeType == ExpressionType.MemberAccess || parent.NodeType == ExpressionType.Call))
            {
                return GetFieldName(parent, options) + "." + name;
            }
            return name;
        }
        throw new NotSupportedException($"Expected member access, got {node.GetType().Name}");
    }

    private static object GetValue(Expression node, JsonSerializerOptions? options)
    {
        object value;
        if (node is ConstantExpression constExpr)
        {
            value = constExpr.Value;
        }
        else if (node is MemberExpression member)
        {
            // Evaluate variable closure
            var objectMember = Expression.Convert(node, typeof(object));
            var getterLambda = Expression.Lambda<Func<object>>(objectMember);
            var getter = getterLambda.Compile();
            value = getter();
        }
        else 
        {
            throw new NotSupportedException($"Expected constant or member value, got {node.GetType().Name}");
        }

        // Handle Enum conversion based on options
        if (value is Enum enumValue)
        {
             // Check if string serialization is requested
             // This is a simplistic check. Ideally we check specific converter on property or global list.
             // We will check global converters for now.
             bool useString = false;
             if (options != null)
             {
                 if (options.Converters.Any(c => c is JsonStringEnumConverter))
                 {
                     useString = true;
                 }
             }

             if (useString)
             {
                 return enumValue.ToString();
             }
             else
             {
                 // Return underlying type (usually int)
                 return Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()));
             }
        }

        return value;
    }
}
