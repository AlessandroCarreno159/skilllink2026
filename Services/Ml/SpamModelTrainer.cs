using Microsoft.ML;

namespace SkillLink_dotnet.Services.Ml;

public sealed record SpamEjemplo(string Texto, bool EsSpam);

public sealed record ResultadoEntrenamiento(
    int Filas, int Spam, int Ham, double Accuracy, double Auc, double F1);

public static class SpamModelTrainer
{
    public static ResultadoEntrenamiento Entrenar(
        string csvPath,
        string modeloPath,
        IEnumerable<SpamEjemplo>? ejemplosExtra = null)
    {
        var filas = LeerCsv(csvPath);
        if (ejemplosExtra is not null)
        {
            foreach (var ejemplo in ejemplosExtra)
            {
                if (!string.IsNullOrWhiteSpace(ejemplo.Texto))
                    filas.Add(new SpamInput { Texto = ejemplo.Texto.Trim(), EsSpam = ejemplo.EsSpam });
            }
        }
        if (filas.Count < 20)
            throw new InvalidOperationException($"Datos insuficientes para entrenar: {filas.Count} filas.");

        var ml = new MLContext(seed: 1);
        var data = ml.Data.LoadFromEnumerable(filas);
        var split = ml.Data.TrainTestSplit(data, testFraction: 0.2, seed: 1);

        var pipeline = ml.Transforms.Text.FeaturizeText("Features", nameof(SpamInput.Texto))
            .Append(ml.Transforms.NormalizeMinMax("Features"))
            .Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression(
                labelColumnName: nameof(SpamInput.EsSpam),
                featureColumnName: "Features"));

        var modelo = pipeline.Fit(split.TrainSet);
        var predicciones = modelo.Transform(split.TestSet);
        var metricas = ml.BinaryClassification.Evaluate(
            predicciones, labelColumnName: nameof(SpamInput.EsSpam));

        var directorio = Path.GetDirectoryName(modeloPath);
        if (!string.IsNullOrWhiteSpace(directorio)) Directory.CreateDirectory(directorio);
        ml.Model.Save(modelo, split.TrainSet.Schema, modeloPath);

        var spam = filas.Count(f => f.EsSpam);
        return new ResultadoEntrenamiento(
            filas.Count,
            spam,
            filas.Count - spam,
            metricas.Accuracy,
            metricas.AreaUnderRocCurve,
            metricas.F1Score);
    }

    private static List<SpamInput> LeerCsv(string ruta)
    {
        if (!File.Exists(ruta))
            throw new FileNotFoundException("No se encontró el dataset semilla.", ruta);

        var filas = new List<SpamInput>();
        foreach (var linea in File.ReadLines(ruta).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(linea)) continue;
            var separador = linea.LastIndexOf(',');
            if (separador <= 0) continue;
            var texto = linea[..separador].Trim();
            var etiqueta = linea[(separador + 1)..].Trim();
            if (texto.Length == 0) continue;
            filas.Add(new SpamInput
            {
                Texto = texto,
                EsSpam = etiqueta.Equals("true", StringComparison.OrdinalIgnoreCase) || etiqueta == "1"
            });
        }
        return filas;
    }
}
