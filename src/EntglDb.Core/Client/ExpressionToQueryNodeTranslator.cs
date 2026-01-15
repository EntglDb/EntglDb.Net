using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace EntglDb.Core
{
    /// <summary>
    /// Translates LINQ Expression trees to <see cref="QueryNode"/> for database querying.
    /// </summary>
    public static class ExpressionToQueryNodeTranslator
    {
        /// <summary>
        /// Translates a LINQ predicate expression to a QueryNode.
        /// </summary>
        /// <returns>A QueryNode representing the query, or null if the predicate is always true.</returns>
        public static QueryNode Translate<T>(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) return null;
            return Visit(predicate.Body);
        }

        private static QueryNode Visit(Expression node)
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
                    return new And(Visit(and.Left), Visit(and.Right));

                case ExpressionType.OrElse:
                    var or = (BinaryExpression)node;
                    return new Or(Visit(or.Left), Visit(or.Right));

                case ExpressionType.Equal:
                    var eq = (BinaryExpression)node;
                    return new Eq(GetFieldName(eq.Left), GetValue(eq.Right));

                case ExpressionType.GreaterThan:
                    var gt = (BinaryExpression)node;
                    return new Gt(GetFieldName(gt.Left), GetValue(gt.Right));

                case ExpressionType.LessThan:
                    var lt = (BinaryExpression)node;
                    return new Lt(GetFieldName(lt.Left), GetValue(lt.Right));

                case ExpressionType.GreaterThanOrEqual:
                    var gte = (BinaryExpression)node;
                    return new Gte(GetFieldName(gte.Left), GetValue(gte.Right));

                case ExpressionType.LessThanOrEqual:
                    var lte = (BinaryExpression)node;
                    return new Lte(GetFieldName(lte.Left), GetValue(lte.Right));

                case ExpressionType.NotEqual:
                    var neq = (BinaryExpression)node;
                    return new Neq(GetFieldName(neq.Left), GetValue(neq.Right));

                case ExpressionType.Call:
                     var call = (MethodCallExpression)node;
                     if (call.Method.Name == "Contains")
                     {
                         if (call.Object != null && call.Object.Type == typeof(string))
                         {
                             return new Contains(GetFieldName(call.Object), (string)GetValue(call.Arguments[0]));
                         }
                     }
                     break;
            }

            throw new NotSupportedException($"Expression type {node.NodeType} is not supported");
        }

        private static string GetFieldName(Expression node)
        {
            if (node.NodeType == ExpressionType.Convert)
            {
                return GetFieldName(((UnaryExpression)node).Operand);
            }

            if (node is MemberExpression member)
            {
                var name = member.Member.Name;
                var parent = member.Expression;

                if (parent != null && (parent.NodeType == ExpressionType.MemberAccess || parent.NodeType == ExpressionType.Call))
                {
                    return GetFieldName(parent) + "." + name;
                }
                return name;
            }
            throw new NotSupportedException($"Expected member access, got {node.GetType().Name}");
        }

        private static object GetValue(Expression node)
        {
            if (node is ConstantExpression constExpr)
            {
                return constExpr.Value;
            }
            if (node is MemberExpression member)
            {
                // Evaluate variable closure
                var objectMember = Expression.Convert(node, typeof(object));
                var getterLambda = Expression.Lambda<Func<object>>(objectMember);
                var getter = getterLambda.Compile();
                return getter();
            }
            throw new NotSupportedException($"Expected constant or member value, got {node.GetType().Name}");
        }
    }
}
