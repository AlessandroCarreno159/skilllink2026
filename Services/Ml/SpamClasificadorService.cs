using Microsoft.AspNetCore.Hosting;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace SkillLink_dotnet.Services.Ml;

public sealed class SpamClasificadorService
{
    private readonly ILogger<SpamClasificadorService> _logger;
    private readonly string _spamPath;
    private readonly string _basuraPath;
    private readonly string _peligrosoPath;
    private readonly bool _habilitado;
    private readonly object _sync = new();
    private PredictionEngine<SpamInput, SpamPrediction>? _spamEngine;
    private PredictionEngine<SpamInput, SpamPrediction>? _basuraEngine;
    private PredictionEngine<SpamInput, SpamPrediction>? _peligrosoEngine;

    public bool ModeloCargado =>
        _spamEngine is not null || _basuraEngine is not null || _peligrosoEngine is not null;

    public string Modo => _spamEngine is not null && _basuraEngine is not null && _peligrosoEngine is not null
        ? "ML"
        : ModeloCargado ? "Parcial" : "Heurístico";

    public SpamClasificadorService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<SpamClasificadorService> logger)
    {
        _logger = logger;
        _habilitado = configuration.GetValue("Ml:Enabled", true);
        _spamPath = ResolverRuta(configuration["Ml:SpamModelPath"] ?? "ML/spam-model.zip", environment);
        _basuraPath = ResolverRuta(configuration["Ml:BasuraModelPath"] ?? "ML/basura-model.zip", environment);
        _peligrosoPath = ResolverRuta(configuration["Ml:PeligrosoModelPath"] ?? "ML/peligroso-model.zip", environment);

        if (!_habilitado)
        {
            _logger.LogWarning("Clasificador deshabilitado (Ml:Enabled=false). Se usarán heurísticas.");
            return;
        }
        CargarModelos();
    }

    public RiesgoContenido Predecir(string? texto)
    {
        var limpio = texto?.Trim() ?? string.Empty;
        var basuraHeuristica = BasuraHeuristics.Calcular(limpio);
        var peligrosoHeuristica = PeligrosoHeuristics.Calcular(limpio);
        if (limpio.Length == 0) return new RiesgoContenido(1, 0, 1, 0, "Basura");

        if (!ModeloCargado)
            return Clasificar(SpamHeuristics.Calcular(limpio), basuraHeuristica, peligrosoHeuristica);

        double spam;
        double basura;
        double peligroso;
        lock (_sync)
        {
            spam = Predecir(_spamEngine, limpio, "spam") ?? SpamHeuristics.Calcular(limpio);
            basura = Predecir(_basuraEngine, limpio, "basura") ?? basuraHeuristica;
            peligroso = Predecir(_peligrosoEngine, limpio, "peligroso") ?? peligrosoHeuristica;
        }

        // Las heurísticas son una red de seguridad para casos obvios aunque el modelo falle.
        if (basuraHeuristica >= 0.7) basura = Math.Max(basura, basuraHeuristica);
        if (peligrosoHeuristica >= 0.55) peligroso = Math.Max(peligroso, peligrosoHeuristica);
        return Clasificar(spam, basura, peligroso);
    }

    public void RecargarModelo()
    {
        lock (_sync)
        {
            _spamEngine?.Dispose();
            _basuraEngine?.Dispose();
            _peligrosoEngine?.Dispose();
            _spamEngine = null;
            _basuraEngine = null;
            _peligrosoEngine = null;
            if (_habilitado) CargarModelos();
        }
    }

    private static RiesgoContenido Clasificar(double spam, double basura, double peligroso)
    {
        spam = Math.Clamp(spam, 0, 1);
        basura = Math.Clamp(basura, 0, 1);
        peligroso = Math.Clamp(peligroso, 0, 1);

        var clase = "Normal";
        if (peligroso >= SpamRiesgo.UmbralPeligroso)
            clase = "Peligroso";
        else if (spam >= SpamRiesgo.UmbralClase && basura >= SpamRiesgo.UmbralBasura)
            clase = spam >= basura - SpamRiesgo.MargenClase ? "Spam" : "Basura";
        else if (spam >= SpamRiesgo.UmbralClase) clase = "Spam";
        else if (basura >= SpamRiesgo.UmbralBasura) clase = "Basura";

        var probabilidad = Math.Max(spam, Math.Max(basura, peligroso));
        if (clase == "Basura") probabilidad = Math.Max(probabilidad, SpamRiesgo.PisoBasura);
        if (clase == "Peligroso") probabilidad = Math.Max(probabilidad, SpamRiesgo.PisoPeligroso);
        return new RiesgoContenido(probabilidad, spam, basura, peligroso, clase);
    }

    private void CargarModelos()
    {
        _spamEngine = CargarModelo(_spamPath, "spam");
        _basuraEngine = CargarModelo(_basuraPath, "basura");
        _peligrosoEngine = CargarModelo(_peligrosoPath, "peligroso");
    }

    private PredictionEngine<SpamInput, SpamPrediction>? CargarModelo(string ruta, string nombre)
    {
        if (!File.Exists(ruta))
        {
            _logger.LogWarning("Modelo de {Nombre} no encontrado en {Ruta}. Se usará su heurística.", nombre, ruta);
            return null;
        }
        try
        {
            var ml = new MLContext();
            var modelo = ml.Model.Load(ruta, out _);
            var engine = ml.Model.CreatePredictionEngine<SpamInput, SpamPrediction>(modelo);
            _logger.LogInformation("Modelo de {Nombre} cargado desde {Ruta}.", nombre, ruta);
            return engine;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el modelo de {Nombre} en {Ruta}. Se usará su heurística.", nombre, ruta);
            return null;
        }
    }

    private double? Predecir(PredictionEngine<SpamInput, SpamPrediction>? engine, string texto, string nombre)
    {
        if (engine is null) return null;
        try
        {
            var salida = engine.Predict(new SpamInput { Texto = texto });
            var probabilidad = float.IsNaN(salida.Probability) ? salida.Score : salida.Probability;
            return Math.Clamp(probabilidad, 0, 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al predecir {Nombre}; se usará su heurística.", nombre);
            return null;
        }
    }

    private static string ResolverRuta(string ruta, IWebHostEnvironment environment) =>
        Path.IsPathRooted(ruta) ? ruta : Path.Combine(environment.ContentRootPath, ruta);
}

public sealed class SpamInput
{
    [LoadColumn(0)] public string Texto { get; set; } = string.Empty;
    [LoadColumn(1)] public bool EsSpam { get; set; }
}

public sealed class SpamPrediction
{
    [ColumnName("PredictedLabel")] public bool EsSpam { get; set; }
    public float Score { get; set; }
    public float Probability { get; set; }
}

public sealed record RiesgoContenido(
    double Probabilidad,
    double ProbabilidadSpam,
    double ProbabilidadBasura,
    double ProbabilidadPeligroso,
    string Clase)
{
    public string Nivel => SpamRiesgo.Nivel(Probabilidad);
    public string Badge => SpamRiesgo.Badge(Probabilidad);
    public string BadgeClase => Clase switch
    {
        "Peligroso" => "dark",
        "Spam" => "danger",
        "Basura" => "warning",
        _ => "success",
    };
}

public static class SpamRiesgo
{
    public const double Umbral = 0.50;
    public const double UmbralClase = 0.50;
    public const double UmbralBasura = 0.45;
    public const double UmbralPeligroso = 0.50;
    public const double UmbralBloqueo = 0.70;
    public const double PisoBasura = 0.50;
    public const double PisoPeligroso = 0.60;
    public const double MargenClase = 0.05;

    public static string Nivel(double probabilidad) => probabilidad switch
    {
        >= 0.75 => "Alto",
        >= 0.50 => "Medio",
        _ => "Bajo",
    };

    public static string Badge(double probabilidad) => Nivel(probabilidad) switch
    {
        "Alto" => "danger",
        "Medio" => "warning",
        _ => "success",
    };
}
