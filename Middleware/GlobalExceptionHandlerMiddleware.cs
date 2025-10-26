using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using FlowerInventory.Services;

namespace FlowerInventory.Middleware
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            var response = context.Response;

            var errorResponse = new
            {
                success = false,
                message = "系統發生錯誤",
                error = exception.Message,
                requestId = context.TraceIdentifier
            };

            switch (exception)
            {
                case InventoryServiceException:
                    // 業務邏輯例外
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse = new
                    {
                        success = false,
                        message = exception.Message,
                        error = "業務邏輯錯誤",
                        requestId = context.TraceIdentifier
                    };
                    _logger.LogWarning(exception, "業務邏輯例外: {Message}", exception.Message);
                    break;

                case DbUpdateException:
                    // 資料庫更新例外
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse = new
                    {
                        success = false,
                        message = "資料庫操作失敗",
                        error = "請檢查輸入資料是否正確",
                        requestId = context.TraceIdentifier
                    };
                    _logger.LogError(exception, "資料庫更新例外");
                    break;

                case UnauthorizedAccessException:
                    // 未授權存取
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    errorResponse = new
                    {
                        success = false,
                        message = "未授權存取",
                        error = "請登入系統",
                        requestId = context.TraceIdentifier
                    };
                    break;

                default:
                    // 未預期的系統例外
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    _logger.LogError(exception, "未預期的系統例外: {Message}", exception.Message);
                    break;
            }

            var result = JsonSerializer.Serialize(errorResponse);
            await context.Response.WriteAsync(result);
        }
    }

    // 擴充方法以便在 Program.cs 中使用
    public static class GlobalExceptionHandlerMiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        }
    }
}
