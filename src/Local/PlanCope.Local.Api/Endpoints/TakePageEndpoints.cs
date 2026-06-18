using System.Net;
using PlanCope.Local.Api.Services;

namespace PlanCope.Local.Api.Endpoints;

public static class TakePageEndpoints
{
    public static IEndpointRouteBuilder MapTakePageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Redirect("/examen"));
        endpoints.MapGet("/examen", ServeStudentApp);
        endpoints.MapGet("/examen/{sessionId}", ServeStudentApp);
        endpoints.MapGet("/toma/{sessionId}", (string sessionId) => Results.Redirect($"/examen/{WebUtility.UrlEncode(sessionId)}"));
        endpoints.MapGet("/take", () => Results.Redirect("/examen"));
        endpoints.MapGet("/take/{sessionId}", (string sessionId) => Results.Redirect($"/examen/{WebUtility.UrlEncode(sessionId)}"));

        return endpoints;
    }

    private static IResult ServeStudentApp()
    {
        var indexPath = LocalClientAppFiles.FindIndexPath();
        return indexPath is not null
            ? Results.File(indexPath, "text/html; charset=utf-8")
            : Results.Problem("No se encontro la interfaz React compilada para la toma de alumnos.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
