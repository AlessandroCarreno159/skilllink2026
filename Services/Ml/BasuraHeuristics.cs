using System.Text.RegularExpressions;

namespace SkillLink_dotnet.Services.Ml;

public static class BasuraHeuristics
{
    private static readonly string[] FrasesVacias =
    {
        "no se", "no sé", "nada", "nada que ver", "no aplica", "sin informacion", "sin información",
        "no tengo idea", "lo que sea", "cualquier cosa", "hola", "buenas", "texto de prueba", "prueba"
    };

    private static readonly string[] RolODetalle =
    {
        "gasfitero", "electricista", "maestro", "albañil", "cocinero", "mesero", "chofer", "conductor",
        "diseñador", "disenador", "programador", "desarrollador", "developer", "vendedor", "ventas",
        "limpieza", "niñera", "niera", "enfermera", "contador", "administrador", "recepcionista",
        "seguridad", "almacen", "logistica", "logística", "soldador", "carpintero", "jardinero",
        "barista", "fotografo", "fotógrafo", "redactor", "marketing", "community", "soporte",
        "miraflores", "surco", "san isidro", "lima", "callao", "ate", "cusco", "piura", "trujillo",
        "arequipa", "remoto", "presencial", "hibrido", "híbrido", "turno", "medio tiempo",
        "tiempo completo", "por horas", "fines de semana", "experiencia", "años", "anos",
        "sueldo", "pago", "salario", "horario", "disponibilidad"
    };

    private static readonly Regex SoloSimbolos = new(@"^[^\p{L}]+$", RegexOptions.Compiled);
    private static readonly Regex Teclado = new(@"(asd|sda|dsa|qwe|wer|zxc|xcv|hjkl|1234|ñlk|lkñ)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Repeticion = new(@"(.)\1{4,}", RegexOptions.Compiled);
    private static readonly Regex BuscoVago = new(
        @"^(busco|necesito|quiero)\s+(chamba|trabajo|empleo|pega|plata|dinero|sueldo|algo)\s*[.!?]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BuscoVago2 = new(
        @"^(busco|necesito|quiero)\s+(chamba|trabajo|empleo|pega)\s+(de\s+)?(lo\s+que\s+sea|cualquier\s+cosa|algo)\s*[.!?]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Lorem = new(@"lorem ipsum", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Separador = new(@"\s+", RegexOptions.Compiled);

    public static double Calcular(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 1;
        var t = texto.Trim();
        var minusculas = t.ToLowerInvariant();
        var palabras = Separador.Split(minusculas).Where(p => p.Length > 0).ToArray();
        var letras = t.Count(char.IsLetter);

        if (SoloSimbolos.IsMatch(t)) return 0.9;
        if (letras < 3) return 0.85;
        if (Teclado.IsMatch(minusculas)) return 0.85;
        if (Repeticion.IsMatch(t)) return 0.75;
        if (BuscoVago.IsMatch(minusculas) || BuscoVago2.IsMatch(minusculas)) return 0.85;
        if (Lorem.IsMatch(minusculas)) return 0.8;
        if (FrasesVacias.Any(f => minusculas == f || (palabras.Length <= 3 && minusculas.Contains(f)))) return 0.75;

        var mencionaBusqueda = minusculas.Contains("busco") || minusculas.Contains("chamba") ||
            minusculas.Contains("trabajo") || minusculas.Contains("empleo") || minusculas.Contains("pega");
        if (palabras.Length <= 3 && mencionaBusqueda && !RolODetalle.Any(p => minusculas.Contains(p)))
            return 0.7;

        if (palabras.Length >= 5)
        {
            var unicas = palabras.Distinct().Count();
            if (unicas / (double)palabras.Length < 0.35) return 0.65;
        }

        var noLetras = t.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
        if (noLetras / (double)t.Length > 0.5) return 0.6;
        if (palabras.Any(p => p.Length >= 6 && p.Distinct().Count() <= 2)) return 0.55;
        return 0;
    }
}
