using BifitReport.LogViewer.Logs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BifitReport.LogViewer.Infrastructure;

public sealed class LogServiceExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<LogServiceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            LogResourceNotFoundException or FileNotFoundException => StatusCodes.Status404NotFound,
            LogRequestException or AntiforgeryValidationException => StatusCodes.Status400BadRequest,
            LogLimitExceededException => StatusCodes.Status413PayloadTooLarge,
            IOException => StatusCodes.Status503ServiceUnavailable,
            UnauthorizedAccessException => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Ошибка обработки запроса {TraceIdentifier}", context.TraceIdentifier);
        }
        else
        {
            logger.LogWarning("Запрос {TraceIdentifier} отклонён: {Reason}", context.TraceIdentifier, exception.Message);
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = status switch
                {
                    StatusCodes.Status400BadRequest => "Некорректный запрос",
                    StatusCodes.Status404NotFound => "Ресурс не найден",
                    StatusCodes.Status413PayloadTooLarge => "Превышен лимит",
                    StatusCodes.Status503ServiceUnavailable => "Журнал временно недоступен",
                    _ => "Внутренняя ошибка"
                },
                Detail = status >= 500 ? "Используйте идентификатор запроса для поиска в журнале сервиса." : exception.Message,
                Extensions = { ["traceId"] = context.TraceIdentifier }
            }
        });
    }
}
