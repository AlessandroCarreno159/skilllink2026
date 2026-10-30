using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Services.Ml;

public static class DenunciasSistema
{
    public const string Id = "sistema";
}

public sealed class ModeracionService(
    ApplicationDbContext db,
    SpamClasificadorService clasificador)
{
    public string Modo => clasificador.Modo;

    public RiesgoContenido Evaluar(string? texto) => clasificador.Predecir(texto);

    public bool BloqueaPublicacion(RiesgoContenido r) =>
        r.Clase == "Peligroso" && r.ProbabilidadPeligroso >= SpamRiesgo.UmbralBloqueo;

    public async Task RegistrarSiRiesgosoAsync(
        string objetoTipo, int objetoId, string autorId, RiesgoContenido riesgo)
    {
        if (riesgo.Probabilidad < SpamRiesgo.Umbral) return;

        var motivo = riesgo.Clase switch
        {
            "Spam" => "spam",
            "Peligroso" => "peligroso",
            "Basura" => "basura",
            _ => "otro"
        };

        var existe = await db.Denuncias.AnyAsync(d =>
            d.DenuncianteId == DenunciasSistema.Id &&
            d.ObjetoTipo == objetoTipo &&
            d.ObjetoId == objetoId &&
            d.Estado == "pendiente");
        if (!existe)
        {
            db.Denuncias.Add(new Denuncia
            {
                DenuncianteId = DenunciasSistema.Id,
                ObjetoTipo = objetoTipo,
                ObjetoId = objetoId,
                Motivo = motivo,
                Descripcion =
                    $"Detección automática ({clasificador.Modo}): {riesgo.Clase} {riesgo.Probabilidad:P0} " +
                    $"(spam {riesgo.ProbabilidadSpam:P0}, peligroso {riesgo.ProbabilidadPeligroso:P0}, basura {riesgo.ProbabilidadBasura:P0}).",
                Estado = "pendiente"
            });
        }

        if (riesgo.Clase is "Spam" or "Peligroso")
        {
            var autor = await db.Users.FindAsync(autorId);
            if (autor is not null) autor.Sospechoso = true;
        }

        await db.SaveChangesAsync();
    }

    public async Task AplicarResolucionAsync(Denuncia d, string estado)
    {
        var ocultar = estado is "resuelta" or "basura" or "peligroso";
        if (!ocultar) return;

        string? autorId = null;
        if (d.ObjetoTipo == "publicacion")
        {
            var pub = await db.Publicaciones.FindAsync(d.ObjetoId);
            if (pub is not null)
            {
                pub.Activa = false;
                autorId = pub.AutorId;
            }
        }
        else if (d.ObjetoTipo == "comentario")
        {
            var com = await db.ComentariosHilo.FindAsync(d.ObjetoId);
            if (com is not null)
            {
                com.Activo = false;
                autorId = com.AutorId;
            }
        }

        if (autorId is not null)
        {
            var autor = await db.Users.FindAsync(autorId);
            if (autor is not null)
            {
                autor.FaltasModeracion++;
                if (autor.FaltasModeracion >= 3) autor.Sospechoso = true;
            }
        }
    }
}
