---
layout: default
title: Querying
---

# Querying

EntglDb allows querying local collections using a rich set of operators and idiomatic C# syntax, including LINQ support.

## Basic Querying

You can query documents using the `Find` method on a `PeerCollection`.

```csharp
var users = await peerStore.GetCollection<User>("users");

// Precise match
var fabio = await users.Find(u => u.FirstName == "Fabio");

// Comparisons
var adults = await users.Find(u => u.Age >= 18);

// Logical Operators
var activeAdmins = await users.Find(u => u.IsActive && u.Role == "Admin");
```

## Serialization Consistency

EntglDb respects your configured serialization settings. If you use `snake_case` in your JSON serialization but standard C# PascalCase properties, the query translator automatically handles the mapping.

```csharp
// Definition
public class User 
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; }
}

// Query
// This translates to a SQL query checking json_extract(data, '$.first_name')
var result = await users.Find(u => u.FirstName == "Fabio");
```

## Supported Operators

- `==` (Equal)
- `!=` (Not Equal)
- `>` (Greater Than)
- `<` (Less Than)
- `>=` (Greater Than or Equal)
- `<=` (Less Than or Equal)
- `&&` (AND)
- `||` (OR)
