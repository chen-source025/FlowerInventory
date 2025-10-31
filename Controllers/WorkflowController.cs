using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlowerInventory.Models;
using FlowerInventory.Services;
using FlowerInventory.Utilities;
using FlowerInventory.ViewModels;
using System.ComponentModel.DataAnnotations;

namespace FlowerInventory.Controllers
{
    public class WorkflowController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEnhancedInventoryService _inventoryService;
        private readonly ILogger<WorkflowController> _logger;

        public WorkflowController(
            ApplicationDbContext context,
            IEnhancedInventoryService inventoryService,
            ILogger<WorkflowController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET: 作業流程儀表板
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var inventoryStatus = await _inventoryService.GetEnhancedInventoryStatusAsync();
                var dashboardData = await GetDashboardDataAsync(inventoryStatus);

                _logger.LogInformation("作業儀表板載入成功，共 {FlowerCount} 種花卉", inventoryStatus.Count);
                return View(dashboardData);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入作業儀表板");
                return View(new WorkflowDashboardViewModel());
            }
        }

        // GET: 進貨作業
        public async Task<IActionResult> ReceiveGoods()
        {
            try
            {
                var flowers = await _context.Flowers
                    .OrderBy(f => f.Name)
                    .ToListAsync();

                ViewBag.Flowers = flowers;
                return View();
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入進貨作業頁面");
                return this.RedirectWithError("Dashboard", "系統載入時發生錯誤");
            }
        }

        // POST: 進貨作業
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveGoods(Batch batch)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("進貨作業模型驗證失敗");
                    await ReloadFlowersViewBag();
                    return View(batch);
                }

                // 驗證花卉存在
                var flower = await _context.Flowers.FindAsync(batch.FlowerId);
                if (flower == null)
                {
                    ModelState.AddModelError("FlowerId", "指定的花卉不存在");
                    await ReloadFlowersViewBag();
                    return View(batch);
                }

                // 自動產生批號
                if (string.IsNullOrEmpty(batch.BatchNo))
                {
                    batch.BatchNo = GenerateBatchNumber(flower);
                }

                // 自動計算到期日
                if (!batch.ExpiryDate.HasValue && flower.ShelfLifeDays.HasValue && flower.ShelfLifeDays > 0)
                {
                    batch.ExpiryDate = batch.ReceivedDate.AddDays(flower.ShelfLifeDays.Value);
                }

                // 初始狀態設定
                batch.QuantityPassed = 0; // 等待品檢
                batch.Status = BatchStatus.Received;
                batch.CreatedDate = DateTime.UtcNow;

                _context.Add(batch);
                await _context.SaveChangesAsync();

                _logger.LogInformation("成功建立進貨批次，批號: {BatchNo}, 花卉: {FlowerName}",
                    batch.BatchNo, flower.Name);

                this.SetSuccessMessage($"✅ 進貨成功！批號: {batch.BatchNo}。請進行品檢作業。");
                return RedirectToAction("Inspection", new { batchId = batch.Id });
            }
            catch (DbUpdateException ex)
            {
                this.LogAndSetError(_logger, ex, "進貨作業");
                this.SetErrorMessage("進貨作業失敗，請檢查輸入資料");
                await ReloadFlowersViewBag();
                return View(batch);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "進貨作業");
                this.SetErrorMessage($"進貨作業失敗: {ex.Message}");
                await ReloadFlowersViewBag();
                return View(batch);
            }
        }

        // GET: 品檢作業
        public async Task<IActionResult> Inspection(int batchId)
        {
            try
            {
                var batch = await _context.Batches
                    .Include(b => b.Flower)
                    .FirstOrDefaultAsync(b => b.Id == batchId);

                if (batch == null)
                {
                    _logger.LogWarning("品檢作業找不到指定的批次，批次ID: {BatchId}", batchId);
                    return this.RedirectWithError("Dashboard", "指定的批次不存在");
                }

                // 檢查批次狀態是否可進行品檢
                if (batch.Status != BatchStatus.Received)
                {
                    _logger.LogWarning("批次狀態不允許品檢，批次ID: {BatchId}, 當前狀態: {Status}",
                        batchId, batch.Status);
                    return this.RedirectWithError("Dashboard", "此批次無法進行品檢作業");
                }

                // 取得當前庫存資訊
                var currentStock = await _inventoryService.CalculateCurrentStockAsync(batch.FlowerId);
                var safetyStock = await _inventoryService.CalculateEnhancedSafetyStockAsync(batch.Flower);

                var model = new InspectionViewModel
                {
                    Batch = batch,
                    ExpectedPassRate = batch.Flower.InspectionPassRate,
                    ExpectedPassQuantity = (int)(batch.QuantityReceived * batch.Flower.InspectionPassRate),
                    CurrentStock = currentStock,
                    SafetyStock = safetyStock,
                    StockStatus = GetStockStatus(currentStock, safetyStock)
                };

                _logger.LogInformation("載入品檢作業頁面，批次ID: {BatchId}, 花卉: {FlowerName}",
                    batchId, batch.Flower.Name);

                return View(model);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入品檢作業頁面");
                return this.RedirectWithError("Dashboard", "系統載入時發生錯誤");
            }
        }

        // POST: 品檢作業
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessInspection(int batchId, int passedQuantity, string inspectionNote)
        {
            try
            {
                if (passedQuantity < 0)
                {
                    _logger.LogWarning("品檢作業輸入無效的合格數量: {PassedQuantity}", passedQuantity);
                    this.SetErrorMessage("合格數量不能為負數");
                    return RedirectToAction("Inspection", new { batchId });
                }

                if (string.IsNullOrWhiteSpace(inspectionNote))
                {
                    _logger.LogWarning("品檢作業未填寫備註");
                    this.SetErrorMessage("品檢備註不能為空");
                    return RedirectToAction("Inspection", new { batchId });
                }

                var result = await _inventoryService.ProcessInspectionAsync(batchId, passedQuantity, inspectionNote);

                if (result.Success)
                {
                    _logger.LogInformation("品檢作業成功，批次ID: {BatchId}, 合格數量: {PassedQuantity}/{ReceivedQuantity}",
                        batchId, passedQuantity, result.ReceivedQty);

                    this.SetSuccessMessage(result.Message);

                    // 檢查是否需要補貨
                    var batch = await _context.Batches
                        .Include(b => b.Flower)
                        .FirstOrDefaultAsync(b => b.Id == batchId);

                    if (batch != null)
                    {
                        var recommendation = await _inventoryService.GetReplenishmentRecommendationAsync(batch.FlowerId);
                        if (recommendation.NeedReplenishment)
                        {
                            this.SetWarningMessage(
                                $"⚠️ 庫存預警: {batch.Flower.Name} 需要補貨！建議訂購量: {recommendation.SuggestedOrderQuantity}");
                        }
                    }
                }
                else
                {
                    _logger.LogError("品檢作業失敗，批次ID: {BatchId}, 錯誤: {ErrorMessage}",
                        batchId, result.Message);
                    this.SetErrorMessage(result.Message);
                }

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "品檢作業處理");
                this.SetErrorMessage($"品檢作業失敗: {ex.Message}");
                return RedirectToAction("Inspection", new { batchId });
            }
        }

        // GET: 出貨作業
        public async Task<IActionResult> ShipGoods()
        {
            try
            {
                var allFlowers = await _context.Flowers
                    .Include(f => f.Batches)
                    .OrderBy(f => f.Name)
                    .ToListAsync();

                var flowersWithStock = new List<Flower>();
                foreach (var flower in allFlowers)
                {
                    var stock = await _inventoryService.CalculateCurrentStockAsync(flower.Id);
                    if (stock > 0)
                    {
                        flowersWithStock.Add(flower);
                    }
                }

                var model = new ShipmentViewModel
                {
                    AvailableFlowers = flowersWithStock
                };

                return View(model);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入出貨作業頁面");
                return this.RedirectWithError("Dashboard", "系統載入時發生錯誤");
            }
        }

        // POST: 出貨作業
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ShipGoods(ShipmentViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("出貨作業模型驗證失敗");
                    await ReloadFlowersViewBag();
                    return View(model);
                }

                // 檢查花卉是否存在
                var flower = await _context.Flowers
                    .Include(f => f.Batches)
                    .FirstOrDefaultAsync(f => f.Id == model.FlowerId);

                if (flower == null)
                {
                    ModelState.AddModelError("", "花卉不存在");
                    await ReloadFlowersViewBag();
                    return View(model);
                }

                // 檢查庫存是否足夠 (使用非同步版本)
                var currentStock = await _inventoryService.CalculateCurrentStockAsync(flower.Id);
                if (currentStock < model.Quantity)
                {
                    ModelState.AddModelError("Quantity",
                        $"庫存不足。當前庫存: {currentStock}，出貨數量: {model.Quantity}");
                    await ReloadFlowersViewBag();
                    return View(model);
                }

                if (model.Quantity <= 0)
                {
                    ModelState.AddModelError("Quantity", "出貨數量必須大於0");
                    await ReloadFlowersViewBag();
                    return View(model);
                }

                // 建立出貨交易紀錄
                var transaction = new Transaction
                {
                    FlowerId = model.FlowerId,
                    ChangeQty = -model.Quantity,
                    TransactionType = TransactionType.Out,
                    TransactionDate = DateTime.UtcNow,
                    Note = $"出貨: {model.Quantity} - {model.Reason}" +
                        (string.IsNullOrEmpty(model.CustomerName) ? "" : $" (客戶: {model.CustomerName})")
                };

                _context.Transactions.Add(transaction);
                await _context.SaveChangesAsync();

                // 檢查是否需要補貨
                var recommendation = await _inventoryService.GetReplenishmentRecommendationAsync(model.FlowerId);

                _logger.LogInformation("出貨作業成功，花卉: {FlowerName}, 數量: {Quantity}",
                    flower.Name, model.Quantity);

                this.SetSuccessMessage($"✅ 出貨成功！數量: {model.Quantity}");

                if (recommendation.NeedReplenishment)
                {
                    this.SetWarningMessage(
                        $"⚠️ 庫存預警: {recommendation.FlowerName} 需要補貨！建議訂購量: {recommendation.SuggestedOrderQuantity}");
                }

                return RedirectToAction("Dashboard");
            }
            catch (DbUpdateException ex)
            {
                this.LogAndSetError(_logger, ex, "出貨作業");
                this.SetErrorMessage("出貨作業失敗，請檢查輸入資料");
                await ReloadFlowersViewBag();
                return View(model);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "出貨作業");
                this.SetErrorMessage($"出貨作業失敗: {ex.Message}");
                await ReloadFlowersViewBag();
                return View(model);
            }
        }

        // GET: 庫存調整
        public async Task<IActionResult> AdjustStock()
        {
            try
            {
                var flowers = await _context.Flowers
                    .OrderBy(f => f.Name)
                    .ToListAsync();

                var model = new StockAdjustmentViewModel
                {
                    AvailableFlowers = flowers
                };

                return View(model);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入庫存調整頁面");
                return this.RedirectWithError("Dashboard", "系統載入時發生錯誤");
            }
        }

        // POST: 庫存調整
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustStock(StockAdjustmentViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("庫存調整模型驗證失敗");
                    await ReloadFlowersViewBag();
                    return View(model);
                }

                var result = await _inventoryService.AdjustStockAsync(
                    model.FlowerId, model.AdjustmentQuantity, model.Reason);

                if (result.Success)
                {
                    _logger.LogInformation("庫存調整成功，花卉: {FlowerName}, 調整數量: {AdjustmentQuantity}",
                        result.FlowerName, model.AdjustmentQuantity);

                    this.SetSuccessMessage(result.Message);

                    // 檢查調整後的庫存狀態
                    var recommendation = await _inventoryService.GetReplenishmentRecommendationAsync(model.FlowerId);
                    if (recommendation.NeedReplenishment)
                    {
                        this.SetWarningMessage(
                            $"⚠️ 庫存預警: {result.FlowerName} 需要補貨！建議訂購量: {recommendation.SuggestedOrderQuantity}");
                    }
                }
                else
                {
                    _logger.LogError("庫存調整失敗，花卉ID: {FlowerId}, 錯誤: {ErrorMessage}",
                        model.FlowerId, result.Message);
                    this.SetErrorMessage(result.Message);
                }

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "庫存調整");
                this.SetErrorMessage($"庫存調整失敗: {ex.Message}");
                await ReloadFlowersViewBag();
                return View(model);
            }
        }

        // GET: 補貨建議報告
        public async Task<IActionResult> ReplenishmentReport()
        {
            try
            {
                var inventoryStatus = await _inventoryService.GetEnhancedInventoryStatusAsync();
                var recommendations = inventoryStatus
                    .Where(i => i.Recommendation?.NeedReplenishment == true)
                    .OrderByDescending(i => i.Recommendation?.RecommendationLevel)
                    .ThenBy(i => i.CurrentStock / i.SafetyStock)
                    .ToList();

                ViewBag.TotalRecommendations = recommendations.Count;
                ViewBag.UrgentCount = recommendations.Count(r =>
                    r.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.URGENT ||
                    r.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.CRITICAL);
                ViewBag.SuggestedCount = recommendations.Count(r =>
                    r.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.SUGGESTED);

                _logger.LogInformation("載入補貨建議報告，共 {Count} 項建議", recommendations.Count);
                return View(recommendations);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入補貨建議報告");
                return View(new List<Services.EnhancedInventoryViewModel>());
            }
        }

        // GET: 需求分析報告
        public async Task<IActionResult> DemandAnalysis()
        {
            try
            {
                var analysis = await _inventoryService.GetDemandAnalysisAsync();
                _logger.LogInformation("載入需求分析報告，共 {Count} 項分析", analysis.Count);
                return View(analysis);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入需求分析報告");
                return View(new List<FlowerDemandAnalysis>());
            }
        }

        // GET: 庫存總覽
        public async Task<IActionResult> InventoryOverview()
        {
            try
            {
                var inventoryStatus = await _inventoryService.GetEnhancedInventoryStatusAsync();

                ViewBag.TotalValue = inventoryStatus.Sum(i => i.TotalValue);
                ViewBag.TotalItems = inventoryStatus.Sum(i => i.CurrentStock);
                ViewBag.LowStockCount = inventoryStatus.Count(i => i.CurrentStock < i.SafetyStock);
                ViewBag.OutOfStockCount = inventoryStatus.Count(i => i.CurrentStock == 0);

                _logger.LogInformation("載入庫存總覽，共 {Count} 種花卉", inventoryStatus.Count);
                return View(inventoryStatus);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入庫存總覽");
                return View(new List<Services.EnhancedInventoryViewModel>());
            }
        }

        // GET: 批次管理
        public async Task<IActionResult> BatchManagement()
        {
            try
            {
                var batches = await _context.Batches
                    .Include(b => b.Flower)
                    .OrderByDescending(b => b.ReceivedDate)
                    .ToListAsync();

                ViewBag.ExpiringBatches = batches.Count(b => b.IsExpiringSoon);
                ViewBag.ExpiredBatches = batches.Count(b => b.IsExpired);
                ViewBag.TotalBatches = batches.Count;

                _logger.LogInformation("載入批次管理頁面，共 {Count} 筆批次", batches.Count);
                return View(batches);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入批次管理頁面");
                return View(new List<Batch>());
            }
        }

        // API: 批次品檢狀態檢查
        [HttpGet]
        public async Task<IActionResult> CheckBatchStatus(int batchId)
        {
            try
            {
                var batch = await _context.Batches
                    .Include(b => b.Flower)
                    .FirstOrDefaultAsync(b => b.Id == batchId);

                if (batch == null)
                {
                    return this.ApiSuccess(new { exists = false, message = "批次不存在" });
                }

                var canInspect = batch.Status == BatchStatus.Received;
                var statusInfo = new
                {
                    exists = true,
                    canInspect = canInspect,
                    flowerName = batch.Flower.Name,
                    receivedQty = batch.QuantityReceived,
                    currentStatus = batch.Status.ToString(),
                    statusDescription = GetBatchStatusDescription(batch.Status)
                };

                return this.ApiSuccess(statusInfo);
            }
            catch (Exception ex)
            {
                return this.ApiError(_logger, ex, "檢查批次狀態");
            }
        }

        // API: 取得花卉庫存資訊 (使用非同步版本)
        [HttpGet]
        public async Task<IActionResult> GetFlowerStockInfo(int flowerId)
        {
            try
            {
                var flower = await _context.Flowers
                    .Include(f => f.Batches)
                    .FirstOrDefaultAsync(f => f.Id == flowerId);

                if (flower == null)
                {
                    return this.ApiSuccess(new { exists = false });
                }

                var currentStock = await _inventoryService.CalculateCurrentStockAsync(flowerId);
                var recommendation = await _inventoryService.GetReplenishmentRecommendationAsync(flowerId);

                var stockInfo = new
                {
                    exists = true,
                    flowerName = flower.Name,
                    currentStock = currentStock,
                    safetyStock = recommendation.SafetyStock,
                    needsReplenishment = recommendation.NeedReplenishment,
                    recommendationLevel = recommendation.RecommendationLevel
                };

                return this.ApiSuccess(stockInfo);
            }
            catch (Exception ex)
            {
                return this.ApiError(_logger, ex, "取得花卉庫存資訊");
            }
        }

        private async Task<WorkflowDashboardViewModel> GetDashboardDataAsync(List<Services.EnhancedInventoryViewModel> inventoryStatus)
        {
            var recentBatches = await _context.Batches
                .Include(b => b.Flower)
                .OrderByDescending(b => b.ReceivedDate)
                .Take(5)
                .ToListAsync();

            var recentTransactions = await _context.Transactions
                .Include(t => t.Flower)
                .OrderByDescending(t => t.TransactionDate)
                .Take(10)
                .ToListAsync();

            return new WorkflowDashboardViewModel
            {
                InventoryStatus = inventoryStatus,
                TotalItems = inventoryStatus.Count,
                NeedReplenishmentCount = inventoryStatus.Count(i => i.Recommendation?.NeedReplenishment == true),
                UrgentReplenishmentCount = inventoryStatus.Count(i =>
                    i.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.URGENT ||
                    i.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.CRITICAL),
                RecentBatches = recentBatches,
                RecentTransactions = recentTransactions,
                TotalInventoryValue = inventoryStatus.Sum(i => i.TotalValue)
            };
        }

        private async Task ReloadFlowersViewBag()
        {
            ViewBag.Flowers = await _context.Flowers
                .OrderBy(f => f.Name)
                .ToListAsync();
        }

        private string GenerateBatchNumber(Flower flower)
        {
            var prefix = flower.Name.Length > 0 ? flower.Name.Substring(0, 1).ToUpper() : "F";
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            return $"{prefix}{timestamp}";
        }

        private string GetBatchStatusDescription(BatchStatus status)
        {
            return status switch
            {
                BatchStatus.Received => "已收貨，待品檢",
                BatchStatus.Inspected => "已品檢",
                BatchStatus.Active => "有效庫存",
                BatchStatus.Expired => "已過期",
                BatchStatus.Discarded => "已報廢",
                _ => "未知狀態"
            };
        }

        private string GetStockStatus(int currentStock, double safetyStock)
        {
            if (currentStock == 0) return "缺貨";
            if (currentStock < safetyStock) return "需補貨";
            return "正常";
        }
    }
}
