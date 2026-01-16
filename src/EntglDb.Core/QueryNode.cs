using System.Text.Json;

namespace EntglDb.Core
{
    public abstract class QueryNode { }

    public class Eq : QueryNode
    {
        public string Field { get; }
        public object Value { get; }
        public Eq(string field, object value) { Field = field; Value = value; }
    }

    public class Gt : QueryNode
    {
        public string Field { get; }
        public object Value { get; }
        public Gt(string field, object value) { Field = field; Value = value; }
    }

    public class Lt : QueryNode
    {
        public string Field { get; }
        public object Value { get; }
        public Lt(string field, object value) { Field = field; Value = value; }
    }

    public class Gte : QueryNode
    {
        public string Field { get; }
        public object Value { get; }
        public Gte(string field, object value) { Field = field; Value = value; }
    }

    public class Lte : QueryNode
    {
        public string Field { get; }
        public object Value { get; }
        public Lte(string field, object value) { Field = field; Value = value; }
    }

    public class Neq : QueryNode
    {
        public string Field { get; }
        public object Value { get; }
        public Neq(string field, object value) { Field = field; Value = value; }
    }

    public class In : QueryNode
    {
        public string Field { get; }
        public object[] Values { get; }
        public In(string field, object[] values) { Field = field; Values = values; }
    }

    public class Contains : QueryNode
    {
        public string Field { get; }
        public string Value { get; }
        public Contains(string field, string value) { Field = field; Value = value; }
    }

    public class NotContains : QueryNode
    {
        public string Field { get; }
        public string Value { get; }
        public NotContains(string field, string value) { Field = field; Value = value; }
    }

    public class And : QueryNode
    {
        public QueryNode Left { get; }
        public QueryNode Right { get; }
        public And(QueryNode left, QueryNode right) { Left = left; Right = right; }
    }

    public class Or : QueryNode
    {
        public QueryNode Left { get; }
        public QueryNode Right { get; }
        public Or(QueryNode left, QueryNode right) { Left = left; Right = right; }
    }
}
