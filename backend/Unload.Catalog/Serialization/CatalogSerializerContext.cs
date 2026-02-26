using System.Text.Json.Serialization;

namespace Unload.Catalog;

[JsonSerializable(typeof(CatalogRoot))]
internal partial class CatalogSerializerContext : JsonSerializerContext;
