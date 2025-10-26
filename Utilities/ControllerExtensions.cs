using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FlowerInventory.Utilities
{
    public static class ControllerExtensions
    {
        // 統一的錯誤處理方法
        public static void LogAndSetError(this Controller controller, ILogger logger, Exception ex, string operation)
        {
            logger.LogError(ex, "{Operation} 時發生錯誤", operation);
            controller.TempData["ErrorMessage"] = $"{operation}失敗，請稍後再試";
        }

        // 統一的成功訊息設定
        public static void SetSuccessMessage(this Controller controller, string message)
        {
            controller.TempData["SuccessMessage"] = message;
        }

        // 統一的警告訊息設定
        public static void SetWarningMessage(this Controller controller, string message)
        {
            controller.TempData["WarningMessage"] = message;
        }

        // 統一的錯誤訊息設定
        public static void SetErrorMessage(this Controller controller, string message)
        {
            controller.TempData["ErrorMessage"] = message;
        }

        // 統一的重導向錯誤方法
        public static IActionResult RedirectWithError(this Controller controller, string actionName, string errorMessage)
        {
            controller.TempData["ErrorMessage"] = errorMessage;
            return controller.RedirectToAction(actionName);
        }

        // 統一的重導向成功方法 (2個參數版本)
        public static IActionResult RedirectWithSuccess(this Controller controller, string actionName, string successMessage)
        {
            controller.TempData["SuccessMessage"] = successMessage;
            return controller.RedirectToAction(actionName);
        }

        // 統一的重導向成功方法 (3個參數版本)
        public static IActionResult RedirectWithSuccess(this Controller controller, string actionName, string successMessage, object routeValues)
        {
            controller.TempData["SuccessMessage"] = successMessage;
            return controller.RedirectToAction(actionName, routeValues);
        }

        // 統一的 API 錯誤回應
        public static IActionResult ApiError(this Controller controller, ILogger logger, Exception ex, string operation)
        {
            logger.LogError(ex, "API {Operation} 時發生錯誤", operation);
            return controller.Json(new
            {
                success = false,
                message = $"{operation}失敗",
                error = ex.Message
            });
        }

        // 統一的 API 成功回應
        public static IActionResult ApiSuccess(this Controller controller, object? data = null, string message = "操作成功")
        {
            return controller.Json(new
            {
                success = true,
                message = message,
                data = data
            });
        }
    }
}
