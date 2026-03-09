using System.Text.Json.Serialization;

namespace Unload.Catalog;

/// <summary>
/// Source-generation контекст сериализации каталога.
/// Используется сервисом каталога для безопасной и быстрой десериализации JSON.
/// </summary>
[JsonSerializable(typeof(CatalogRoot))]
[JsonSerializable(typeof(CatalogBigScript))]
internal partial class CatalogSerializerContext : JsonSerializerContext;
