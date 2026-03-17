using System.Text.Json.Serialization;
using EntglDb.Core.Network;

namespace EntglDb.Core;

/// <summary>
/// JSON source-generation context for EntglDb.Core types.
/// Enables fully AOT-compatible, reflection-free serialization for internal use.
/// </summary>
[JsonSerializable(typeof(OAuth2Configuration))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
internal sealed partial class EntglDbCoreJsonContext : JsonSerializerContext
{
}
