using Algolia.Search.Clients;
using Algolia.Search.Models.Search;
using SkillLink_dotnet.Services;

namespace SkillLink.Services;

/// <summary>
/// Búsqueda externa con Algolia para publicaciones.
/// Si falta configuración (AppId/ApiKey), IsConfigured es false y todas las
/// operaciones son no-op: los controladores usan la BD como fallback.
/// Ningún método lanza: los fallos de red se registran y se devuelven vacíos.
/// </summary>
public sealed class AlgoliaService
{
    private const string FiltroActivas = "activa:true";

    private readonly SearchClient? _client;
    private readonly string _indexName;
    private readonly string _perfilesIndexName;
    private readonly ILogger<AlgoliaService> _logger;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly SemaphoreSlim _perfilesSettingsLock = new(1, 1);
    private bool _settingsApplied;
    private bool _perfilesSettingsApplied;

    public bool IsConfigured => _client is not null;

    public AlgoliaService(IConfiguration configuration, ILogger<AlgoliaService> logger)
    {
        _logger = logger;
        _indexName = configuration["Algolia:IndexName"] ?? "skilllink_index";
        _perfilesIndexName = configuration["Algolia:PerfilesIndexName"] ?? $"{_indexName}_perfiles";
        var appId = configuration["Algolia:ApplicationId"];
        // Clave privilegiada solo para servidor. "Algolia:ApiKey" se mantiene
        // como alias temporal para despliegues que todavía no migraron.
        var apiKey = configuration["Algolia:IndexingApiKey"]
                     ?? configuration["Algolia:ApiKey"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Algolia sin configurar (ApplicationId/IndexingApiKey). Se usará la BD como fallback.");
            return;
        }
        if (string.IsNullOrWhiteSpace(configuration["Algolia:IndexingApiKey"])
            && !string.IsNullOrWhiteSpace(configuration["Algolia:ApiKey"]))
        {
            _logger.LogWarning("Algolia: use Algolia:IndexingApiKey en lugar del alias obsoleto Algolia:ApiKey.");
        }
        _client = new SearchClient(appId, apiKey);
    }

    /// <summary>
    /// Aplica una sola vez los ajustes de relevancia del índice.
    /// Solo el título, las habilidades y la categoría/tipo son buscables;
    /// el contenido queda fuera de searchableAttributes para evitar coincidencias
    /// accidentales en descripciones genéricas.
    /// </summary>
    public async Task EnsureSettingsAsync()
    {
        if (_client is null || _settingsApplied) return;

        await _settingsLock.WaitAsync();
        try
        {
            if (_settingsApplied) return;

            var settings = new IndexSettings
            {
                SearchableAttributes = new List<string>
                {
                    "titulo",
                    "unordered(habilidades)",
                    "unordered(categoriaLabel)",
                    "unordered(categoria)",
                    "unordered(tipoLabel)",
                    "unordered(tipo)"
                },
                AttributesForFaceting = new List<string>
                {
                    "filterOnly(activa)",
                    "filterOnly(categoria)",
                    "filterOnly(modalidad)",
                    "filterOnly(tipo)"
                },
                CustomRanking = new List<string>
                {
                    "desc(activa)",
                    "desc(fechaTimestamp)"
                },
                AttributesToRetrieve = new List<string>
                {
                    "objectID", "titulo", "contenido", "habilidades",
                    "categoria", "categoriaLabel", "modalidad", "modalidadLabel",
                    "tipo", "tipoLabel", "precio", "imagen", "imagenUrl",
                    "ciudad", "autor", "fecha", "fechaTimestamp", "activa"
                }
            };

            await _client.SetSettingsAsync(_indexName, settings);
            _settingsApplied = true;
            _logger.LogInformation("Algolia: searchableAttributes y ranking aplicados al índice {Index}.", _indexName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudieron aplicar los ajustes del índice {Index}.", _indexName);
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <summary>Ajustes del índice de perfiles (personas y empresas).</summary>
    public async Task EnsurePerfilesSettingsAsync()
    {
        if (_client is null || _perfilesSettingsApplied) return;

        await _perfilesSettingsLock.WaitAsync();
        try
        {
            if (_perfilesSettingsApplied) return;

            var settings = new IndexSettings
            {
                SearchableAttributes = new List<string>
                {
                    "nombre",
                    "unordered(detalle)",
                    "unordered(habilidades)",
                    "unordered(descripcion)",
                    "unordered(ciudad)"
                },
                AttributesForFaceting = new List<string>
                {
                    "filterOnly(estado)",
                    "filterOnly(tipoRegistro)"
                },
                AttributesToRetrieve = new List<string>
                {
                    "objectID", "usuarioId", "tipoRegistro", "nombre", "detalle",
                    "descripcion", "habilidades", "ciudad", "disponibilidad",
                    "estado", "imagenUrl", "url"
                }
            };

            await _client.SetSettingsAsync(_perfilesIndexName, settings);
            _perfilesSettingsApplied = true;
            _logger.LogInformation("Algolia: ajustes aplicados al índice de perfiles {Index}.", _perfilesIndexName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudieron aplicar los ajustes del índice de perfiles {Index}.", _perfilesIndexName);
        }
        finally
        {
            _perfilesSettingsLock.Release();
        }
    }

    /// <summary>Elimina todos los registros del índice (se usa antes de un reindexado completo).</summary>
    public async Task<bool> ClearIndexAsync()
    {
        if (_client is null) return false;
        try
        {
            var response = await _client.ClearObjectsAsync(_indexName);
            if (response is { TaskID: > 0 })
                await _client.WaitForTaskAsync(_indexName, response.TaskID);
            _logger.LogInformation("Algolia: índice {Index} vaciado.", _indexName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudo vaciar el índice {Index}.", _indexName);
            return false;
        }
    }

    public async Task IndexRecordAsync<T>(T record) where T : class
    {
        if (_client is null || record is null) return;
        await EnsureSettingsAsync();
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
        var imagenRelativa = string.IsNullOrWhiteSpace(p.Imagen)
            ? null
            : p.Imagen.TrimStart('/');

        var record = new Dictionary<string, object?>
        {
            ["objectID"] = p.Id.ToString(),
            ["titulo"] = p.Titulo,
            ["contenido"] = p.Contenido,
            ["habilidades"] = p.HabilidadesReq,
            ["categoria"] = p.Categoria,
            ["categoriaLabel"] = Catalogos.Categoria(p.Categoria),
            ["modalidad"] = p.Modalidad,
            ["modalidadLabel"] = Catalogos.Modalidad(p.Modalidad),
            ["tipo"] = p.Tipo,
            ["tipoLabel"] = Catalogos.Tipo(p.Tipo),
            ["precio"] = Catalogos.Precio(p),
            ["moneda"] = Catalogos.MonedaBase(p),
            ["precioDual"] = p.PrecioDual,
            ["aceptaDolares"] = Catalogos.EsUsd(p.Moneda) || p.PrecioDual,
            ["etiquetaMoneda"] = Catalogos.EtiquetaMoneda(p),
            ["imagen"] = imagenRelativa,
            ["imagenUrl"] = imagenRelativa is null ? "/img/placeholder.svg" : $"/uploads/{imagenRelativa}",
            ["ciudad"] = p.Autor?.Perfil?.Ciudad ?? p.Autor?.Empresa?.Ciudad,
            ["autor"] = p.Autor is null ? null : ImageUploadService.NombrePublico(p.Autor),
            ["fecha"] = p.FechaCreacion.ToString("O"),
            ["fechaTimestamp"] = new DateTimeOffset(DateTime.SpecifyKind(p.FechaCreacion, DateTimeKind.Utc)).ToUnixTimeSeconds(),
            ["activa"] = p.Activa,
        };
        return IndexRecordAsync(record);
    }

    /// <summary>Indexa varias publicaciones (por ejemplo, tras editar datos del autor).</summary>
    public async Task IndexPublicacionesAsync(IEnumerable<SkillLink_dotnet.Models.Publicacion> publicaciones)
    {
        foreach (var p in publicaciones)
            await IndexPublicacionAsync(p);
    }

    /// <summary>Mantiene el índice alineado con el estado Activa de la publicación.</summary>
    public Task SyncPublicacionAsync(SkillLink_dotnet.Models.Publicacion p) =>
        p.Activa ? IndexPublicacionAsync(p) : DeletePublicacionAsync(p.Id);

    /// <summary>
    /// Vacía el índice y vuelve a indexar solo las publicaciones activas recibidas.
    /// Se usa en reindexados masivos/seed para no dejar registros inactivos.
    /// </summary>
    public async Task<int> ReindexActivasAsync(IEnumerable<SkillLink_dotnet.Models.Publicacion> publicaciones)
    {
        if (_client is null) return 0;
        await EnsureSettingsAsync();
        // Si no se pudo vaciar, abortar: indexar sobre un índice con residuos
        // podría dejar publicaciones inactivas visibles.
        if (!await ClearIndexAsync()) return 0;
        var total = 0;
        foreach (var p in publicaciones)
        {
            await IndexPublicacionAsync(p);
            total++;
        }
        return total;
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
        await EnsureSettingsAsync();
        try
        {
            var resp = await _client.SearchSingleIndexAsync<AlgoliaHit>(
                _indexName,
                new SearchParams(new SearchParamsObject
                {
                    Query = query,
                    HitsPerPage = limit,
                    Filters = FiltroActivas
                }));
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

    /// <summary>Indexa/actualiza el perfil de un trabajador.</summary>
    public async Task IndexPerfilAsync(SkillLink_dotnet.Models.Perfil p, string estadoCuenta)
    {
        if (_client is null) return;
        await EnsurePerfilesSettingsAsync();
        var record = new Dictionary<string, object?>
        {
            ["objectID"] = $"perfil-{p.UsuarioId}",
            ["usuarioId"] = p.UsuarioId,
            ["tipoRegistro"] = "persona",
            ["nombre"] = p.NombreCompleto,
            ["detalle"] = p.Especialidad,
            ["descripcion"] = p.Descripcion,
            ["habilidades"] = p.Habilidades,
            ["ciudad"] = p.Ciudad,
            ["disponibilidad"] = p.Disponibilidad,
            ["estado"] = estadoCuenta,
            ["imagenUrl"] = ImagenUrl(p.Foto),
            ["url"] = $"/trabajador/perfil/{p.UsuarioId}"
        };
        await SavePerfilRecordAsync(record, p.UsuarioId);
    }

    /// <summary>Indexa/actualiza el perfil de una empresa.</summary>
    public async Task IndexEmpresaAsync(SkillLink_dotnet.Models.Empresa e, string estadoCuenta)
    {
        if (_client is null) return;
        await EnsurePerfilesSettingsAsync();
        var record = new Dictionary<string, object?>
        {
            ["objectID"] = $"empresa-{e.UsuarioId}",
            ["usuarioId"] = e.UsuarioId,
            ["tipoRegistro"] = "empresa",
            ["nombre"] = e.RazonSocial,
            ["detalle"] = e.Sector,
            ["descripcion"] = e.Descripcion,
            ["habilidades"] = null,
            ["ciudad"] = e.Ciudad,
            ["disponibilidad"] = null,
            ["estado"] = estadoCuenta,
            ["imagenUrl"] = ImagenUrl(e.Logo),
            ["url"] = $"/empresa/perfil/{e.UsuarioId}"
        };
        await SavePerfilRecordAsync(record, e.UsuarioId);
    }

    /// <summary>Sincroniza el perfil indexado según el rol y estado actual del usuario.</summary>
    public async Task SyncPerfilUsuarioAsync(SkillLink_dotnet.Models.ApplicationUser u)
    {
        if (_client is null) return;
        switch (u.Rol)
        {
            case SkillLink_dotnet.Models.Roles.Trabajador when u.Perfil is not null:
                await IndexPerfilAsync(u.Perfil, u.EstadoCuenta);
                break;
            case SkillLink_dotnet.Models.Roles.Empresa when u.Empresa is not null:
                await IndexEmpresaAsync(u.Empresa, u.EstadoCuenta);
                break;
            default:
                await DeletePerfilUsuarioAsync(u.Id);
                break;
        }
    }

    /// <summary>Elimina del índice de perfiles los registros de un usuario.</summary>
    public async Task DeletePerfilUsuarioAsync(string usuarioId)
    {
        if (_client is null) return;
        try
        {
            await _client.DeleteObjectAsync(_perfilesIndexName, $"perfil-{usuarioId}");
            await _client.DeleteObjectAsync(_perfilesIndexName, $"empresa-{usuarioId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudo eliminar el perfil del usuario {UsuarioId}.", usuarioId);
        }
    }

    /// <summary>Busca perfiles aprobados en Algolia y devuelve sus UsuarioId en orden de relevancia.
    /// Null si Algolia no está configurado o la búsqueda falló.</summary>
    public async Task<List<string>?> SearchPerfilUsuarioIdsAsync(string query, string tipo, int limit = 50)
    {
        if (_client is null || string.IsNullOrWhiteSpace(query)) return null;
        await EnsurePerfilesSettingsAsync();
        try
        {
            var tipoRegistro = tipo == "empresa" ? "empresa" : "persona";
            var resp = await _client.SearchSingleIndexAsync<AlgoliaHit>(
                _perfilesIndexName,
                new SearchParams(new SearchParamsObject
                {
                    Query = query,
                    HitsPerPage = limit,
                    Filters = $"estado:aprobado AND tipoRegistro:{tipoRegistro}"
                }));
            var ids = new List<string>();
            foreach (var h in resp.Hits ?? Enumerable.Empty<AlgoliaHit>())
                if (!string.IsNullOrWhiteSpace(h.UsuarioId))
                    ids.Add(h.UsuarioId);
            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: búsqueda de perfiles falló.");
            return null;
        }
    }

    /// <summary>Vacía el índice de perfiles y reindexa los usuarios recibidos.</summary>
    public async Task<int> ReindexPerfilesAsync(IEnumerable<SkillLink_dotnet.Models.ApplicationUser> usuarios)
    {
        if (_client is null) return 0;
        await EnsurePerfilesSettingsAsync();
        if (!await ClearPerfilesIndexAsync()) return 0;
        var total = 0;
        foreach (var u in usuarios)
        {
            if (u.Rol == SkillLink_dotnet.Models.Roles.Trabajador && u.Perfil is not null)
            {
                await IndexPerfilAsync(u.Perfil, u.EstadoCuenta);
                total++;
            }
            else if (u.Rol == SkillLink_dotnet.Models.Roles.Empresa && u.Empresa is not null)
            {
                await IndexEmpresaAsync(u.Empresa, u.EstadoCuenta);
                total++;
            }
        }
        return total;
    }

    private async Task<bool> ClearPerfilesIndexAsync()
    {
        if (_client is null) return false;
        try
        {
            var response = await _client.ClearObjectsAsync(_perfilesIndexName);
            if (response is { TaskID: > 0 })
                await _client.WaitForTaskAsync(_perfilesIndexName, response.TaskID);
            _logger.LogInformation("Algolia: índice de perfiles {Index} vaciado.", _perfilesIndexName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudo vaciar el índice de perfiles {Index}.", _perfilesIndexName);
            return false;
        }
    }

    private async Task SavePerfilRecordAsync(Dictionary<string, object?> record, string usuarioId)
    {
        try
        {
            await _client!.SaveObjectAsync(_perfilesIndexName, record);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Algolia: no se pudo indexar el perfil del usuario {UsuarioId}.", usuarioId);
        }
    }

    private static string ImagenUrl(string? relativa)
    {
        if (string.IsNullOrWhiteSpace(relativa)) return ImageUploadService.PlaceholderUrl;
        return $"/uploads/{relativa.TrimStart('/')}";
    }

    private sealed class AlgoliaHit
    {
        [System.Text.Json.Serialization.JsonPropertyName("objectID")]
        public string? ObjectID { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("usuarioId")]
        public string? UsuarioId { get; set; }
    }
}
