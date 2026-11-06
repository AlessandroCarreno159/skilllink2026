using System.ComponentModel.DataAnnotations;

namespace SkillLink_dotnet.Models;

public static class TiposPublicacion
{
    public const string Oferta = "oferta";
    public const string Experiencia = "experiencia";
}

public static class EstadosSolicitud
{
    public const string Pendiente = "pendiente";
    public const string Aceptada = "aceptada";
    public const string Rechazada = "rechazada";
}

// Equivale a tabla `publicaciones`.
public class Publicacion
{
    public int Id { get; set; }
    [Required] public string AutorId { get; set; } = string.Empty;
    public ApplicationUser? Autor { get; set; }

    [Required] public string Tipo { get; set; } = TiposPublicacion.Oferta;
    public string? Titulo { get; set; }
    [Required] public string Contenido { get; set; } = string.Empty;
    public string? Imagen { get; set; }
    public string? HabilidadesReq { get; set; }
    // Campos del mockup (filtros + tarjetas): todos opcionales para no romper datos existentes.
    public string? Categoria { get; set; }      // construccion|tecnologia|diseno|hogar|otros
    public string? Modalidad { get; set; }      // tiempo_completo|medio_tiempo|por_proyecto
    public string? PrecioTexto { get; set; }    // ej. "S/ 80 - S/ 150"
    public int? Vacantes { get; set; }
    public bool Activa { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<Solicitud> Solicitudes { get; set; } = new List<Solicitud>();
    public ICollection<ComentarioPublicacion> ComentariosHilo { get; set; } = new List<ComentarioPublicacion>();
}

// Entidad principal "Solicitud" — adapta `postulaciones` del Flask.
public class Solicitud
{
    public int Id { get; set; }
    public int PublicacionId { get; set; }
    public Publicacion? Publicacion { get; set; }

    [Required] public string TrabajadorId { get; set; } = string.Empty;
    public ApplicationUser? Trabajador { get; set; }

    public string Mensaje { get; set; } = "Postulación con un clic - perfil y experiencia adjuntos.";
    public bool IncluyeCv { get; set; } = true;
    public string Estado { get; set; } = EstadosSolicitud.Pendiente;
    public DateTime FechaPostulacion { get; set; } = DateTime.UtcNow;
    public bool Activa { get; set; } = true;
}
