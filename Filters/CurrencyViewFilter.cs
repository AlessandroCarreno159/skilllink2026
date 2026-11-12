using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SkillLink_dotnet.Services.Currency;

namespace SkillLink_dotnet.Filters;

/// <summary>
/// Deja la tasa PEN→USD en ViewData una sola vez por request (la caché del
/// servicio evita llamadas repetidas). Si no hay tasa, las vistas omiten el
/// precio convertido y muestran solo la moneda base.
/// </summary>
public sealed class CurrencyViewFilter(ExchangeRateService rates) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ViewResult view)
        {
            decimal? rate = null;
            if (rates.IsConfigured)
                rate = await rates.GetPenToUsdAsync(context.HttpContext.RequestAborted);
            view.ViewData["PenToUsd"] = rate;
        }

        await next();
    }
}
