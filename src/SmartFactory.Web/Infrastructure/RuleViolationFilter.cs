using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SmartFactory.Infrastructure.Services;

namespace SmartFactory.Web.Infrastructure;

/// <summary>A rule violation becomes a message on the page the user came from, not an error page.</summary>
public class RuleViolationFilter : IExceptionFilter
{
    private readonly ITempDataDictionaryFactory _tempData;
    public RuleViolationFilter(ITempDataDictionaryFactory tempData) { _tempData = tempData; }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not RuleViolationException ex) return;
        var temp = _tempData.GetTempData(context.HttpContext);
        temp["Error"] = ex.Message;
        var referer = context.HttpContext.Request.Headers.Referer.ToString();
        context.Result = new RedirectResult(string.IsNullOrEmpty(referer) ? (context.HttpContext.Request.PathBase.HasValue ? context.HttpContext.Request.PathBase.Value! : "/") : referer);
        context.ExceptionHandled = true;
    }
}
