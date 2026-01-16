using System;
using System.Collections.Generic;
using System.Text;
using EntglDb.Core;
using Dapper;

namespace EntglDb.Persistence.Sqlite
{
    public class SqlQueryTranslator
    {
        private readonly StringBuilder _sql = new StringBuilder();
        private readonly DynamicParameters _parameters = new DynamicParameters();
        private int _paramCount = 0;

        public (string Sql, DynamicParameters Parameters) Translate(QueryNode query)
        {
            _sql.Clear();
            _parameters.RemoveUnused = false; // Keep all params even if we seemingly don't use them (safety)
            _paramCount = 0;

            if (query == null)
            {
                return ("1=1", _parameters);
            }

            Visit(query);
            return (_sql.ToString(), _parameters);
        }

        private void Visit(QueryNode node)
        {
            switch (node)
            {
                case Eq eq:
                    VisitBinary(eq.Field, "=", eq.Value);
                    break;
                case Gt gt:
                    VisitBinary(gt.Field, ">", gt.Value);
                    break;
                case Lt lt:
                    VisitBinary(lt.Field, "<", lt.Value);
                    break;
                case Gte gte:
                    VisitBinary(gte.Field, ">=", gte.Value);
                    break;
                case Lte lte:
                    VisitBinary(lte.Field, "<=", lte.Value);
                    break;
                case Neq neq:
                    VisitBinary(neq.Field, "<>", neq.Value);
                    break;
                case In inNode: // Changed variable name 'in' to 'inNode'
                    VisitIn(inNode.Field, inNode.Values);
                    break;
                case Contains contains:
                    VisitContains(contains.Field, contains.Value);
                    break;
                case NotContains notContains:
                    VisitNotContains(notContains.Field, notContains.Value);
                    break;
                case And and:
                    _sql.Append("(");
                    Visit(and.Left);
                    _sql.Append(" AND ");
                    Visit(and.Right);
                    _sql.Append(")");
                    break;
                case Or or:
                    _sql.Append("(");
                    Visit(or.Left);
                    _sql.Append(" OR ");
                    Visit(or.Right);
                    _sql.Append(")");
                    break;
                default:
                    throw new NotSupportedException($"Query node type {node.GetType().Name} is not supported.");
            }
        }

        private void VisitBinary(string field, string op, object value)
        {
            string paramName = AddParameter(value);
            _sql.Append($"json_extract(JsonData, '$.{field}') {op} {paramName}");
        }

        private void VisitIn(string field, object[] values)
        {
            if (values == null || values.Length == 0)
            {
                _sql.Append("1=0");
                return;
            }

            var paramNames = new List<string>();
            foreach (var val in values)
            {
                paramNames.Add(AddParameter(val));
            }

            _sql.Append($"json_extract(JsonData, '$.{field}') IN ({string.Join(", ", paramNames)})");
        }

        private void VisitContains(string field, string value)
        {
            string paramName = AddParameter($"%{value}%");
            _sql.Append($"json_extract(JsonData, '$.{field}') LIKE {paramName}");
        }

        private void VisitNotContains(string field, string value)
        {
            string paramName = AddParameter($"%{value}%");
            _sql.Append($"json_extract(JsonData, '$.{field}') NOT LIKE {paramName}");
        }

        private string AddParameter(object value)
        {
            string name = $"@p{_paramCount++}";
            _parameters.Add(name, value);
            return name;
        }
    }
}
