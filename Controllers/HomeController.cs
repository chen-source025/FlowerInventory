using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlowerInventory.Models;
using FlowerInventory.Services;
using FlowerInventory.Utilities;
using System.Diagnostics;

namespace FlowerInventory.Controllers
{
    public class HomeController(
        ILogger<HomeController> logger,
        ApplicationDbContext context,
        IEnhancedInventoryService inventoryService) : Controller
    {
        private readonly ILogger<HomeController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly ApplicationDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
        private readonly IEnhancedInventoryService _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));

        public async Task<IActionResult> Index()
        {
            try
            {
                var inventoryStatus = await _inventoryService.GetEnhancedInventoryStatusAsync();

                var dashboardData = await GetDashboardDataAsync(inventoryStatus);
                SetDashboardViewBag(dashboardData);

                _logger.LogInformation("首頁儀表板載入成功，共 {FlowerCount} 筆花卉資料", inventoryStatus.Count);
                return View();
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入首頁儀表板");
                SetEmptyDashboardViewBag();
                return View();
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var errorViewModel = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };

            _logger.LogError("發生未處理的錯誤, RequestId: {RequestId}", errorViewModel.RequestId);
            return View(errorViewModel);
        }

        // 系統資訊頁面
        public IActionResult About()
        {
            var systemInfo = new
            {
                Version = "1.0.0",
                Framework = ".NET 8.0",
                Database = "PostgreSQL",
                LastUpdated = DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss"),
                Developer = "花卉庫存管理系統團隊",
                Environment = HttpContext.Request.Host.Host.Contains("localhost") ? "開發環境" : "生產環境"
            };

            ViewBag.SystemInfo = systemInfo;
            return View();
        }

        // 快速統計 API
        [HttpGet]
        public async Task<IActionResult> GetQuickStats()
        {
            try
            {
                var inventoryStatus = await _inventoryService.GetEnhancedInventoryStatusAsync();

                var stats = new
                {
                    TotalFlowers = inventoryStatus.Count,
                    UrgentReplenishment = inventoryStatus.Count(i =>
                        i.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.URGENT ||
                        i.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.CRITICAL),
                    LowStock = inventoryStatus.Count(i => i.CurrentStock < i.SafetyStock),
                    TotalValue = inventoryStatus.Sum(i => i.TotalValue).ToString("C0"),
                    ExpiringSoon = inventoryStatus.Sum(i => i.ExpiringBatches.Count(b => b.IsExpiringSoon)),
                    OutOfStock = inventoryStatus.Count(i => i.CurrentStock == 0),
                    LastUpdated = DateTime.UtcNow.ToString("HH:mm:ss")
                };

                _logger.LogDebug("快速統計API呼叫成功");
                return this.ApiSuccess(stats);
            }
            catch (Exception ex)
            {
                return this.ApiError(_logger, ex, "取得快速統計");
            }
        }

        // 健康檢查 API
        [HttpGet]
        public async Task<IActionResult> HealthCheck()
        {
            try
            {
                var dbConnected = await _context.Database.CanConnectAsync();
                var flowerCount = await _context.Flowers.CountAsync();
                var serviceStatus = "正常";

                var healthStatus = new
                {
                    Status = "Healthy",
                    Database = dbConnected ? "Connected" : "Disconnected",
                    FlowersCount = flowerCount,
                    Service = serviceStatus,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    Uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime
                };

                _logger.LogInformation("健康檢查通過");
                return this.ApiSuccess(healthStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "健康檢查失敗");
                return this.ApiError(_logger, ex, "健康檢查");
            }
        }

        // 私有輔助方法
        private async Task<DashboardData> GetDashboardDataAsync(List<EnhancedInventoryViewModel> inventoryStatus)
        {
            var recentBatches = await _context.Batches
                .Include(b => b.Flower)
                .OrderByDescending(b => b.ReceivedDate)
                .Take(5)
                .ToListAsync();

            return new DashboardData
            {
                InventoryStatus = inventoryStatus,
                RecentBatches = recentBatches,
                UrgentItems = inventoryStatus.Count(i =>
                    i.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.URGENT ||
                    i.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.CRITICAL),
                TotalItems = inventoryStatus.Count,
                LowStockItems = inventoryStatus.Count(i => i.CurrentStock < i.SafetyStock),
                OutOfStockItems = inventoryStatus.Count(i => i.CurrentStock == 0),
                TotalValue = inventoryStatus.Sum(i => i.TotalValue)
            };
        }

        private void SetDashboardViewBag(DashboardData data)
        {
            ViewBag.UrgentItems = data.UrgentItems;
            ViewBag.TotalItems = data.TotalItems;
            ViewBag.LowStockItems = data.LowStockItems;
            ViewBag.OutOfStockItems = data.OutOfStockItems;
            ViewBag.TotalValue = data.TotalValue;
            ViewBag.InventoryStatus = data.InventoryStatus.Take(5).ToList();
            ViewBag.RecentBatches = data.RecentBatches;
        }

        private void SetEmptyDashboardViewBag()
        {
            ViewBag.UrgentItems = 0;
            ViewBag.TotalItems = 0;
            ViewBag.LowStockItems = 0;
            ViewBag.OutOfStockItems = 0;
            ViewBag.TotalValue = 0;
            ViewBag.InventoryStatus = new List<EnhancedInventoryViewModel>();
            ViewBag.RecentBatches = new List<Batch>();
        }
    }

    // 儀表板數據類別
    public class DashboardData
    {
        public List<EnhancedInventoryViewModel> InventoryStatus { get; set; } = new();
        public List<Batch> RecentBatches { get; set; } = new();
        public int UrgentItems { get; set; }
        public int TotalItems { get; set; }
        public int LowStockItems { get; set; }
        public int OutOfStockItems { get; set; }
        public decimal TotalValue { get; set; }
    }
}
