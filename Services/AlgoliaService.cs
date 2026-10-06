using Algolia.Search.Clients;
using Algolia.Search.Models.Search;

namespace SkillLink.Services;

/// <summary>
/// Búsqueda externa con Algolia para publicaciones.
/// Si falta configuración (AppId/ApiKey), IsConfigured es false y todas las
/// operaciones son no-op: los controladores usan la BD como fallback.
/// Ningún método lanza: los fallos de red se registran y se devuelven vacíos.
/// </summary>
public sealed class AlgoliaService
{
    private readonly SearchClient? _client;
    private readonly string _indexName;
    private readonly ILogger<AlgoliaService> _logger;

    public bool IsConfigured => _client is not null;

    public AlgoliaService(IConfiguration configuration, ILogger<AlgoliaService> logger)
    {
        _logger = logger;
        _indexName = configuration["Algolia:IndexName"] ?? "skilllink_index";
        var appId = configuration["Algolia:ApplicationId"];
        var apiKey = configuration["Algolia:ApiKey"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Algolia sin configurar (ApplicationId/ApiKey). Se usará la BD como fallback.");
            return;
        }
        _client = new SearchClient(appId, apiKey);
    }

    public async Task IndexRecordAsync<T>(T record) where T : class
    {
        if (_client is null || record is null) return;
        try
        {
            await _client.SaveObjectAsync(_indexName, record);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudo indexar el registro.");
        }
    }

    /// <summary>Indexa/actualiza una publicación (objectID = Id).</summary>
    public Task IndexPublicacionAsync(SkillLink_dotnet.Models.Publicacion p)
    {
        var record = new Dictionary<string, object?>
        {
            ["objectID"] = p.Id.ToString(),
            ["titulo"] = p.Titulo,
            ["contenido"] = p.Contenido,
            ["habilidades"] = p.HabilidadesReq,
            ["categoria"] = p.Categoria,
            ["modalidad"] = p.Modalidad,
            ["tipo"] = p.Tipo,
            ["activa"] = p.Activa,
        };
        return IndexRecordAsync(record);
    }

    /// <summary>Elimina una publicación del índice (no falla si no existe).</summary>
    public async Task DeletePublicacionAsync(int publicacionId)
    {
        if (_client is null) return;
        try
        {
            await _client.DeleteObjectAsync(_indexName, publicacionId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudo eliminar la publicación {Id}.", publicacionId);
        }
    }

    /// <summary>Busca en Algolia y devuelve los Ids de publicación en orden de relevancia.
    /// Null si Algolia no está configurado o la búsqueda falló (usar fallback a BD).</summary>
    public async Task<List<int>?> SearchPublicacionIdsAsync(string query, int limit = 50)
    {
        if (_client is null || string.IsNullOrWhiteSpace(query)) return null;
        try
        {
            var resp = await _client.SearchSingleIndexAsync<AlgoliaHit>(
                _indexName,
                new SearchParams(new SearchParamsObject { Query = query, HitsPerPage = limit }));
            var ids = new List<int>();
            foreach (var h in resp.Hits ?? Enumerable.Empty<AlgoliaHit>())
                if (int.TryParse(h.ObjectID, out var id))
                    ids.Add(id);
            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: búsqueda falló, se usará la BD.");
            return null;
        }
    }

    private sealed class AlgoliaHit
    {
        [System.Text.Json.Serialization.JsonPropertyName("objectID")]
        public string? ObjectID { get; set; }
    }
}
