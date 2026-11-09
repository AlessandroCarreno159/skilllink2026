using System.Text.Json.Nodes;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;

namespace SkillLink_dotnet.Services.Chatbot;

public interface IChatTool
{
    string Name { get; }
    string Description { get; }
    JsonObject Parameters { get; }
    Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct);
}

internal static class ChatToolJson
{
    public static JsonObject Ok(string summary, JsonArray? cards = null, JsonArray? items = null)
    {
        var result = new JsonObject
        {
            ["ok"] = true,
            ["summary"] = summary,
            ["cards"] = cards?.DeepClone() ?? new JsonArray(),
        };
        if (items is not null) result["items"] = items.DeepClone();
        return result;
    }

    public static JsonObject Error(string error) => new()
    {
        ["ok"] = false,
        ["error"] = error,
        ["cards"] = new JsonArray(),
    };

    public static string? Str(JsonNode? args, string name)
    {
        var nodo = args?[name];
        if (nodo is null) return null;
        try { return nodo.GetValue<string>(); }
        catch { return nodo.ToString(); }
    }

    public static int Int(JsonNode? args, string name, int fallback)
    {
        var nodo = args?[name];
        if (nodo is null) return fallback;
        try { return nodo.GetValue<int>(); }
        catch
        {
            return int.TryParse(nodo.ToString(), out var value) ? value : fallback;
        }
    }

    public static List<int> IntList(JsonNode? args, string name)
    {
        var nodo = args?[name];
        if (nodo is not JsonArray array) return [];
        var result = new List<int>();
        foreach (var item in array)
        {
            if (item is null) continue;
            try { result.Add(item.GetValue<int>()); }
            catch
            {
                if (int.TryParse(item.ToString(), out var value)) result.Add(value);
            }
        }
        return result.Distinct().Take(5).ToList();
    }
}

public sealed class ChatCardFactory(ImageUploadService uploads)
{
    public static string NombrePublicoSeguro(ApplicationUser? autor)
    {
        if (autor?.Rol == Roles.Trabajador && !string.IsNullOrWhiteSpace(autor.Perfil?.NombreCompleto))
            return autor.Perfil!.NombreCompleto;
        if (autor?.Rol == Roles.Empresa && !string.IsNullOrWhiteSpace(autor.Empresa?.RazonSocial))
            return autor.Empresa!.RazonSocial;
        return "Usuario de SkillLink";
    }

    public JsonObject Publicacion(Publicacion p, double? rating, int ratingTotal, string? reason)
    {
        var tipo = Catalogos.Tipo(p.Tipo);
        var categoria = Catalogos.Categoria(p.Categoria);
        var modalidad = Catalogos.Modalidad(p.Modalidad);
        var subtitulo = string.Join(" · ", new[] { tipo, categoria, modalidad }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return new JsonObject
        {
            ["type"] = "publicacion",
            ["id"] = p.Id,
            ["title"] = ChatPrivacy.Sanitize(p.Titulo) is { Length: > 0 } titulo
                ? titulo
                : $"Publicación #{p.Id}",
            ["subtitle"] = subtitulo,
            ["city"] = Catalogos.Ciudad(p),
            ["price"] = ChatPrivacy.Sanitize(Catalogos.Precio(p)),
            ["rating"] = rating,
            ["ratingTotal"] = ratingTotal,
            ["imageUrl"] = uploads.Url(p.Imagen),
            ["url"] = $"/Feed/Details/{p.Id}",
            ["reason"] = ChatPrivacy.Sanitize(reason),
            ["peerId"] = p.AutorId,
            ["peerName"] = NombrePublicoSeguro(p.Autor),
            ["skills"] = ChatPrivacy.Sanitize(p.HabilidadesReq),
            ["availability"] = null,
        };
    }

    public JsonObject Tecnico(Perfil perfil, ApplicationUser usuario, double? rating, int ratingTotal, string? reason)
    {
        var nombre = string.IsNullOrWhiteSpace(perfil.NombreCompleto) ? "Técnico" : perfil.NombreCompleto;
        return new JsonObject
        {
            ["type"] = "tecnico",
            ["id"] = perfil.Id,
            ["title"] = ChatPrivacy.Sanitize(nombre),
            ["subtitle"] = ChatPrivacy.Sanitize(perfil.Especialidad),
            ["city"] = ChatPrivacy.Sanitize(perfil.Ciudad),
            ["price"] = null,
            ["rating"] = rating,
            ["ratingTotal"] = ratingTotal,
            ["imageUrl"] = uploads.Url(perfil.Foto),
            ["url"] = ImageUploadService.PerfilUrl(usuario),
            ["reason"] = ChatPrivacy.Sanitize(reason),
            ["peerId"] = usuario.Id,
            ["peerName"] = ChatPrivacy.Sanitize(nombre),
            ["skills"] = ChatPrivacy.Sanitize(perfil.Habilidades),
            ["availability"] = ChatPrivacy.Sanitize(perfil.Disponibilidad),
        };
    }
}

public sealed class ChatToolRegistry(
    IEnumerable<IChatTool> tools,
    ILogger<ChatToolRegistry> logger)
{
    private static readonly HashSet<string> Publicas =
    [
        "buscar_publicaciones",
        "buscar_tecnicos",
        "obtener_publicacion",
        "comparar_publicaciones",
        "faq_plataforma",
    ];

    private readonly Dictionary<string, IChatTool> _tools = tools
        .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IChatTool> ForRole(string role)
    {
        if (role == Roles.Admin)
            return _tools.Values.Where(t => Publicas.Contains(t.Name)).ToList();
        if (role is Roles.Trabajador or Roles.Empresa)
            return _tools.Values.ToList();
        return [];
    }

    public bool CanUse(string role, string name)
        => ForRole(role).Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task<JsonNode> ExecuteAsync(
        ChatToolContext context, string name, JsonNode? args, CancellationToken ct)
    {
        if (!CanUse(context.Role, name))
        {
            logger.LogWarning("Herramienta no permitida {Tool} para el rol {Role}.", name, context.Role);
            return ChatToolJson.Error("Esa herramienta no está disponible para tu tipo de cuenta.");
        }

        if (!_tools.TryGetValue(name, out var tool))
            return ChatToolJson.Error("La herramienta solicitada no existe.");

        try
        {
            return await tool.ExecuteAsync(context, args, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falló la herramienta {Tool} del chatbot.", name);
            return ChatToolJson.Error("No se pudo completar la consulta en este momento.");
        }
    }
}
