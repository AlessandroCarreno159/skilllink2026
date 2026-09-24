using System.ComponentModel.DataAnnotations;

namespace SkillLink_dotnet.Models;

// Valoración cruzada 1-5. Equivale a tabla `comentarios`.
public class ComentarioValoracion
{
    public int Id { get; set; }
    [Required] public string AutorId { get; set; } = string.Empty;
    [Required] public string DestinatarioId { get; set; } = string.Empty;
    [Range(1, 5)] public int? Calificacion { get; set; }
    [Required] public string Comentario { get; set; } = string.Empty;
    public bool Recomendacion { get; set; }
    public bool Activo { get; set; } = true;
    public int? PublicacionId { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

// Hilo por publicación con respuestas. Equivale a `comentarios_publicacion`.
public class ComentarioPublicacion
{
    public int Id { get; set; }
    public int PublicacionId { get; set; }
    public Publicacion? Publicacion { get; set; }
    [Required] public string AutorId { get; set; } = string.Empty;
    public ApplicationUser? Autor { get; set; }
    [Required] public string Comentario { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public int? ParentId { get; set; }
    public string? RespuestaAUsuarioId { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

public class Denuncia
{
    public int Id { get; set; }
    [Required] public string DenuncianteId { get; set; } = string.Empty;
    [Required] public string ObjetoTipo { get; set; } = string.Empty; // comentario|publicacion|usuario
    public int ObjetoId { get; set; }
    [Required] public string Motivo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? Evidencia { get; set; }
    public string Estado { get; set; } = "pendiente";
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

public class Reputacion
{
    [Key] public string UsuarioId { get; set; } = string.Empty;
    public int TotalCalificaciones { get; set; }
    public double PromedioEstrellas { get; set; }
}

public class Notificacion
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    [Required] public string Titulo { get; set; } = string.Empty;
    [Required] public string Mensaje { get; set; } = string.Empty;
    public string? Enlace { get; set; }
    public bool Leida { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

public class HistorialModeracion
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    public string? AdminId { get; set; }
    [Required] public string Accion { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public string? ObjetoTipo { get; set; }
    public int? ObjetoId { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

public class BaneoDni
{
    public int Id { get; set; }
    [Required] public string Dni { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public string? BaneadoPor { get; set; }
    public DateTime FechaBan { get; set; } = DateTime.UtcNow;
}

public class DocumentoAcreditacion
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    [Required] public string Archivo { get; set; } = string.Empty;
    public string? NombreOriginal { get; set; }
    public DateTime FechaSubida { get; set; } = DateTime.UtcNow;
}
