using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;

namespace SkillLink.Tests;

public sealed class CatalogosPrecioTests
{
    [Theory]
    [InlineData("S/90", "S/90")]
    [InlineData("S/80 - S/150", "S/80 - S/150")]
    [InlineData("s/80 - s/150", "S/80 - S/150")]
    [InlineData("US$25", "US$25")]
    [InlineData("us$25 - us$40", "US$25 - US$40")]
    [InlineData("120 - 180", "S/120 - S/180")]
    [InlineData("S/90,5 - S/150,75", "S/90.5 - S/150.75")]
    public void NormalizarPrecio_ReconoceFormatos(string entrada, string esperado)
        => Assert.Equal(esperado, Catalogos.NormalizarPrecio(entrada));

    [Theory]
    [InlineData("", "PEN", null)]
    [InlineData("A convenir", "PEN", null)]
    [InlineData("90", "PEN", "S/90")]
    [InlineData("90 - 180", "PEN", "S/90 - S/180")]
    [InlineData("25", "USD", "US$25")]
    [InlineData("25 - 40", "USD", "US$25 - US$40")]
    public void PrecioParaGuardar_NormalizaSegunMoneda(string entrada, string moneda, string? esperado)
        => Assert.Equal(esperado, Catalogos.PrecioParaGuardar(entrada, moneda));

    [Fact]
    public void PrecioMostrado_PenConDual_MuestraEquivalenteEnUsd()
    {
        var publicacion = new Publicacion
        {
            PrecioTexto = "S/100 - S/200",
            Moneda = Monedas.Pen,
            PrecioDual = true,
        };

        var precio = Catalogos.PrecioMostrado(publicacion, 0.29m);

        Assert.Equal("S/100 - S/200", precio.Principal);
        Assert.Equal("≈ US$29 - US$58", precio.Secundario);
        Assert.Equal("Acepta soles y dólares", precio.EtiquetaMoneda);
    }

    [Fact]
    public void PrecioMostrado_UsdConDual_MuestraEquivalenteEnSoles()
    {
        var publicacion = new Publicacion
        {
            PrecioTexto = "US$29",
            Moneda = Monedas.Usd,
            PrecioDual = true,
        };

        var precio = Catalogos.PrecioMostrado(publicacion, 0.29m);

        Assert.Equal("US$29", precio.Principal);
        Assert.Equal("≈ S/100", precio.Secundario);
        Assert.Equal("Acepta soles y dólares", precio.EtiquetaMoneda);
    }

    [Fact]
    public void PrecioMostrado_SinTasa_OmiteSecundario()
    {
        var publicacion = new Publicacion
        {
            PrecioTexto = "S/100",
            Moneda = Monedas.Pen,
            PrecioDual = true,
        };

        var precio = Catalogos.PrecioMostrado(publicacion, null);

        Assert.Equal("S/100", precio.Principal);
        Assert.Null(precio.Secundario);
    }

    [Fact]
    public void PrecioMostrado_SinPrecio_MuestraAConvenir()
    {
        var publicacion = new Publicacion { PrecioTexto = null, Moneda = Monedas.Pen };

        var precio = Catalogos.PrecioMostrado(publicacion, 0.29m);

        Assert.Equal("A convenir", precio.Principal);
        Assert.Null(precio.Secundario);
        Assert.Equal("Solo soles", precio.EtiquetaMoneda);
    }
}
