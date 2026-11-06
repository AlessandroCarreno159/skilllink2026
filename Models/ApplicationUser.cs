using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace SkillLink_dotnet.Models;

public static class Roles
{
    public const string Trabajador = "trabajador";
    public const string Empresa = "empresa";
    public const string Admin = "admin";
}

public static class EstadosCuenta
{
    public const string Incompleto = "incompleto";
    public const string Pendiente = "pendiente";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";
    public const string Suspendido = "suspendido";
    public const string Baneado = "baneado";
}

/// <summary>
/// Usuario de Identity. UserName = DNI (8 dígitos) para conservar el login del proyecto Flask.
/// </summary>
public class ApplicationUser : IdentityUser
{
    [Required, StringLength(8, MinimumLength = 8)]
    [RegularExpression(@"^\d{8}$", ErrorMessage = "El DNI debe tener 8 dígitos numéricos.")]
    public string Dni { get; set; } = string.Empty;

    [Required]
    public string Rol { get; set; } = Roles.Trabajador;

    public string EstadoCuenta { get; set; } = EstadosCuenta.Incompleto;

    public bool PerfilValidado { get; set; }
    public bool Sospechoso { get; set; }
    public int FaltasModeracion { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoAcceso { get; set; }

    public Perfil? Perfil { get; set; }
    public Empresa? Empresa { get; set; }
}
