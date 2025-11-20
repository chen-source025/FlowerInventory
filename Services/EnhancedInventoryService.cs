using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using FlowerInventory.Models;
using FlowerInventory.Utilities;

namespace FlowerInventory.Services
{
    public class EnhancedInventoryService : IEnhancedInventoryService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EnhancedInventoryService> _logger;
        private readonly TimeSpan _defaultCacheDuration = TimeSpan.FromMinutes(1);
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public EnhancedInventoryService(
            ApplicationDbContext context,
            ILogger<EnhancedInventoryService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // =====================================================
        //  公開 API：庫存總覽 / 補貨建議 / 需求分析
        // =====================================================

        public async Task<List<EnhancedInventoryViewModel>> GetEnhancedInventoryStatusAsync(
            CancellationToken cancellationToken = default)
        {
            const string cacheKey = "InventoryStatus";

            if (_cache.TryGetValue(cacheKey, out List<EnhancedInventoryViewModel>? cachedResult))
            {
                _logger.LogDebug("從快取取得庫存狀態");
                return cachedResult ?? new List<EnhancedInventoryViewModel>();
            }

            using var activity = DiagnosticHelper.StartActivity(nameof(GetEnhancedInventoryStatusAsync));

            try
            {
                var flowers = await _context.Flowers
                    .AsNoTracking()
                    .Include(f => f.Batches)
                    .ToListAsync(cancellationToken);

                _logger.LogInformation("取得 {FlowerCount} 筆花卉資料進行庫存狀態計算", flowers.Count);

                var viewModels = new List<EnhancedInventoryViewModel>();

                // ✅ 全程「逐筆」計算，避免 DbContext 並行問題
                foreach (var flower in flowers)
                {
                    try
                    {
                        var vm = await CreateInventoryViewModelAsync(flower, cancellationToken);
                        if (vm != null)
                        {
                            viewModels.Add(vm);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "建立花卉 {FlowerId} 的庫存視圖模型時發生錯誤", flower.Id);
                    }
                }

                _logger.LogInformation("成功計算 {Count} 筆花卉庫存狀態", viewModels.Count);

                _cache.Set(cacheKey, viewModels, _defaultCacheDuration);
                return viewModels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得庫存狀態時發生錯誤");
                throw new InventoryServiceException("取得庫存狀態失敗", ex);
            }
        }

        public async Task<ReplenishmentRecommendation> GetReplenishmentRecommendationAsync(
            int flowerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var flower = await _context.Flowers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == flowerId, cancellationToken);

                if (flower == null)
                {
                    return CreateErrorRecommendation(
                        flowerId,
                        InventoryConstants.ErrorMessages.FLOWER_NOT_FOUND);
                }

                // ✅ 順序執行，避免多個 EF 查詢同時跑在同一個 DbContext
                var currentStock = await CalculateCurrentStockAsync(flowerId, cancellationToken);
                var weeklyDemand = await CalculateWeeklyDemandAsync(flowerId, cancellationToken);
                var safetyStock = await CalculateEnhancedSafetyStockAsync(flower, cancellationToken);

                var recommendation = new ReplenishmentRecommendation
                {
                    FlowerId = flowerId,
                    FlowerName = flower.Name,
                    CurrentStock = currentStock,
                    SafetyStock = safetyStock,
                    WeeklyDemand = weeklyDemand,
                    NeedReplenishment = currentStock < Math.Ceiling(safetyStock),
                    Success = true,
                    RecommendedOrderDate = DateTime.UtcNow,
                    RecommendationLevel = string.Empty,
                    Reason = string.Empty
                };

                if (recommendation.NeedReplenishment)
                {
                    await CalculateReplenishmentDetailsAsync(
                        recommendation,
                        flower,
                        currentStock,
                        safetyStock,
                        weeklyDemand);
                }
                else
                {
                    SetNoReplenishmentNeeded(recommendation);
                }

                return recommendation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成補貨建議時發生錯誤，花卉ID: {FlowerId}", flowerId);
                return CreateErrorRecommendation(flowerId, ex.Message);
            }
        }

        public async Task<List<FlowerDemandAnalysis>> GetDemandAnalysisAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var flowers = await _context.Flowers
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                var analysis = new List<FlowerDemandAnalysis>();

                foreach (var flower in flowers)
                {
                    try
                    {
                        // ✅ 順序執行避免 DbContext 並行
                        var weeklyDemand = await CalculateWeeklyDemandAsync(flower.Id, cancellationToken);
                        var safetyStock = await CalculateEnhancedSafetyStockAsync(flower, cancellationToken);
                        var stdDev = await CalculateWeeklyDemandStdDevAsync(flower.Id, cancellationToken);

                        var demandPattern = weeklyDemand.GetDemandPattern();
                        var cv = stdDev / Math.Max(weeklyDemand, 0.1);
                        var variabilityLevel = ConstantsExtensions.GetVariability(cv);

                        analysis.Add(new FlowerDemandAnalysis
                        {
                            FlowerId = flower.Id,
                            FlowerName = flower.Name,
                            Category = flower.Category,
                            ABCClass = flower.ABCClass,
                            AvgWeeklyDemand = weeklyDemand,
                            DemandStdDev = stdDev,
                            SafetyStock = safetyStock,
                            ReorderPoint = safetyStock + weeklyDemand,
                            DemandPattern = demandPattern,
                            VariabilityLevel = variabilityLevel,
                            ServiceLevel = 0.95,
                            ReviewFrequencyDays = flower.ABCClass.GetReviewDaysByABCClass()
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "計算每週需求/安全庫存/標準差時發生錯誤, 花卉ID: {FlowerId}", flower.Id);
                    }
                }

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得需求分析時發生錯誤");
                throw new InventoryServiceException("取得需求分析失敗", ex);
            }
        }

        // =====================================================
        //  庫存操作：品檢 / 庫存調整
        // =====================================================

        public async Task<InspectionResult> ProcessInspectionAsync(
            int batchId,
            int passedQuantity,
            string note,
            CancellationToken cancellationToken = default)
        {
            if (batchId <= 0)
                throw new ArgumentException("批次ID必須大於0", nameof(batchId));

            if (passedQuantity < 0)
                throw new ArgumentException("合格數量不能為負數", nameof(passedQuantity));

            if (string.IsNullOrWhiteSpace(note))
                throw new ArgumentException("品檢備註不能為空", nameof(note));

            try
            {
                var batch = await _context.Batches
                    .Include(b => b.Flower)
                    .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

                if (batch == null)
                    throw new ArgumentException(InventoryConstants.ErrorMessages.BATCH_NOT_FOUND, nameof(batchId));

                if (passedQuantity > batch.QuantityReceived)
                    throw new ArgumentException(InventoryConstants.ErrorMessages.INSPECTION_QUANTITY_EXCEEDED);

                // 更新品檢結果
                batch.QuantityPassed = passedQuantity;
                batch.InspectionNote = note;
                batch.Status = BatchStatus.Inspected; // 品檢完成狀態

                // 計算品檢合格率並更新花卉資料
                var passRate = batch.QuantityReceived > 0
                    ? (decimal)passedQuantity / batch.QuantityReceived
                    : 1m;

                batch.Flower.InspectionPassRate = passRate;

                // 建立庫存調整交易紀錄（入庫）
                var adjustmentTransaction = new Transaction
                {
                    FlowerId = batch.FlowerId,
                    BatchId = batchId,
                    ChangeQty = passedQuantity,
                    TransactionType = TransactionType.In,
                    TransactionDate = DateTime.UtcNow,
                    Note = $"品檢合格入庫: {passedQuantity}/{batch.QuantityReceived} - {note}"
                };

                _context.Transactions.Add(adjustmentTransaction);

                await _context.SaveChangesAsync(cancellationToken);

                return new InspectionResult
                {
                    BatchId = batchId,
                    ReceivedQty = batch.QuantityReceived,
                    PassedQty = passedQuantity,
                    PassRate = passRate,
                    Message =
                        $"{InventoryConstants.SuccessMessages.INSPECTION_COMPLETED}: {passedQuantity}/{batch.QuantityReceived} 合格 ({(passRate * 100):F1}%)",
                    Success = true,
                    InspectionDate = DateTime.UtcNow,
                    BatchNumber = batch.BatchNo ?? string.Empty,
                    FlowerName = batch.Flower.Name
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "處理品檢作業時發生錯誤, 批次ID: {BatchId}", batchId);
                return new InspectionResult
                {
                    BatchId = batchId,
                    ReceivedQty = 0,
                    PassedQty = 0,
                    PassRate = 0,
                    Message = $"{InventoryConstants.ErrorMessages.CALCULATION_ERROR}: {ex.Message}",
                    Success = false,
                    InspectionDate = DateTime.UtcNow
                };
            }
        }

        public async Task<StockAdjustmentResult> AdjustStockAsync(
            int flowerId,
            int adjustmentQty,
            string reason,
            CancellationToken cancellationToken = default)
        {
            if (flowerId <= 0)
                throw new ArgumentException("花卉ID必須大於0", nameof(flowerId));

            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("調整原因不能為空", nameof(reason));

            try
            {
                var flower = await _context.Flowers.FindAsync(new object[] { flowerId }, cancellationToken);
                if (flower == null)
                    throw new ArgumentException($"{InventoryConstants.ErrorMessages.FLOWER_NOT_FOUND}: {flowerId}");

                var oldStock = await CalculateCurrentStockAsync(flowerId, cancellationToken);

                // 建立庫存調整交易紀錄（可能正/負）
                var transaction = new Transaction
                {
                    FlowerId = flowerId,
                    ChangeQty = adjustmentQty,
                    TransactionType = TransactionType.Adjust,
                    TransactionDate = DateTime.UtcNow,
                    Note = $"庫存調整: {adjustmentQty} - {reason}"
                };

                _context.Transactions.Add(transaction);
                await _context.SaveChangesAsync(cancellationToken);

                var newStock = await CalculateCurrentStockAsync(flowerId, cancellationToken);

                return new StockAdjustmentResult
                {
                    FlowerId = flowerId,
                    FlowerName = flower.Name,
                    OldStock = oldStock,
                    NewStock = newStock,
                    AdjustmentQty = adjustmentQty,
                    Reason = reason,
                    Success = true,
                    Message = $"{InventoryConstants.SuccessMessages.STOCK_ADJUSTED}: {oldStock} → {newStock}",
                    AdjustmentDate = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "調整庫存時發生錯誤，花卉ID: {FlowerId}", flowerId);
                return new StockAdjustmentResult
                {
                    FlowerId = flowerId,
                    FlowerName = string.Empty,
                    OldStock = 0,
                    NewStock = 0,
                    AdjustmentQty = adjustmentQty,
                    Reason = reason,
                    Success = false,
                    Message = $"{InventoryConstants.ErrorMessages.CALCULATION_ERROR}: {ex.Message}",
                    AdjustmentDate = DateTime.UtcNow
                };
            }
        }

        // =====================================================
        //  報表生成：庫存報表 / ABC 報表
        // =====================================================

        public async Task<InventoryReport> GenerateInventoryReportAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var inventoryStatus = await GetEnhancedInventoryStatusAsync(cancellationToken);

                var replenishmentList = inventoryStatus
                    .Where(i => i.Recommendation?.NeedReplenishment == true)
                    .Select(i => i.Recommendation!)
                    .ToList();

                var criticalItems = inventoryStatus.Count(i =>
                    i.Recommendation?.RecommendationLevel ==
                    InventoryConstants.ReplenishmentLevels.CRITICAL);

                return new InventoryReport
                {
                    ReportDate = DateTime.UtcNow,
                    TotalFlowers = inventoryStatus.Count,
                    TotalItems = inventoryStatus.Sum(i => i.CurrentStock),
                    TotalValue = inventoryStatus.Sum(i => i.TotalValue),
                    LowStockItems = inventoryStatus.Count(i => i.IsLowStock),
                    OutOfStockItems = inventoryStatus.Count(i => i.IsOutOfStock),
                    ExpiringItems = inventoryStatus.Sum(i => i.ExpiringBatches.Count),
                    CriticalItems = criticalItems,
                    InventoryDetails = inventoryStatus,
                    ReplenishmentList = replenishmentList,
                    InventoryInvestment = inventoryStatus.Sum(i => i.TotalValue),
                    ReportPeriod = "即時"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成庫存報表時發生錯誤");
                throw new InventoryServiceException("生成庫存報表失敗", ex);
            }
        }

        public async Task<ABCReport> GenerateABCReportAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var inventoryStatus = await GetEnhancedInventoryStatusAsync(cancellationToken);

                var abcAnalysis = inventoryStatus
                    .Select(i => new ABCAnalysisItem
                    {
                        FlowerId = i.FlowerId,
                        FlowerName = i.Name,
                        Category = i.Category,
                        CurrentStock = i.CurrentStock,
                        UnitPrice = i.Price,
                        TotalValue = i.TotalValue,
                        WeeklyDemand = i.WeeklyDemand,
                        SafetyStock = i.SafetyStock
                    })
                    .OrderByDescending(x => x.TotalValue)
                    .ToList();

                decimal totalValue = abcAnalysis.Sum(x => x.TotalValue);
                decimal cumulative = 0;

                var result = new ABCReport
                {
                    ReportDate = DateTime.UtcNow,
                    TotalValue = totalValue
                };

                foreach (var item in abcAnalysis)
                {
                    cumulative += item.TotalValue;
                    decimal percentage = totalValue > 0 ? (item.TotalValue / totalValue) * 100 : 0;
                    decimal cumulativePercentage = totalValue > 0 ? (cumulative / totalValue) * 100 : 0;

                    string abcClass = cumulativePercentage switch
                    {
                        <= InventoryConstants.ABCClassification.CLASS_A_THRESHOLD =>
                            InventoryConstants.ABCClasses.CLASS_A,
                        <= InventoryConstants.ABCClassification.CLASS_B_THRESHOLD =>
                            InventoryConstants.ABCClasses.CLASS_B,
                        _ => InventoryConstants.ABCClasses.CLASS_C
                    };

                    string strategy = abcClass.GetManagementStrategy();
                    int reviewDays = abcClass.GetReviewDaysByABCClass();

                    var abcItem = new ABCAnalysisItem
                    {
                        FlowerId = item.FlowerId,
                        FlowerName = item.FlowerName,
                        Category = item.Category,
                        CurrentStock = item.CurrentStock,
                        UnitPrice = item.UnitPrice,
                        TotalValue = item.TotalValue,
                        Percentage = percentage,
                        CumulativePercentage = cumulativePercentage,
                        ABCClass = abcClass,
                        ManagementStrategy = strategy,
                        WeeklyDemand = item.WeeklyDemand,
                        SafetyStock = item.SafetyStock,
                        ReviewFrequencyDays = reviewDays
                    };

                    result.Items.Add(abcItem);

                    switch (abcClass)
                    {
                        case InventoryConstants.ABCClasses.CLASS_A:
                            result.ClassACount++;
                            result.ClassAValue += item.TotalValue;
                            break;
                        case InventoryConstants.ABCClasses.CLASS_B:
                            result.ClassBCount++;
                            result.ClassBValue += item.TotalValue;
                            break;
                        case InventoryConstants.ABCClasses.CLASS_C:
                            result.ClassCCount++;
                            result.ClassCValue += item.TotalValue;
                            break;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成ABC分析報表時發生錯誤");
                throw new InventoryServiceException("生成ABC分析報表失敗", ex);
            }
        }

        // =====================================================
        //  計算方法：安全庫存 / 當前庫存 / 週需求 / 標準差
        // =====================================================

        public async Task<double> CalculateEnhancedSafetyStockAsync(
            Flower flower,
            CancellationToken cancellationToken = default)
        {
            if (flower == null)
                throw new ArgumentNullException(nameof(flower));

            try
            {
                // ✅ 改成順序執行，避免 DbContext 並行
                var weeklyDemand = await CalculateWeeklyDemandAsync(flower.Id, cancellationToken);
                var sigma = await CalculateWeeklyDemandStdDevAsync(flower.Id, cancellationToken);

                return CalculateSafetyStockInternal(flower, weeklyDemand, sigma);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "計算安全庫存錯誤 FlowerId={FlowerId}, Name={FlowerName}",
                    flower.Id, flower.Name);

                return flower.ABCClass.GetDefaultDemandByABCClass();
            }
        }

        /// <summary>
        /// ✅ 修正版當前庫存：直接累加 Transactions.ChangeQty
        /// In / Adjust 為正，Out 為負，最後再取 max(0)
        /// </summary>
        public async Task<int> CalculateCurrentStockAsync(
            int flowerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var total = await _context.Transactions
                    .Where(t => t.FlowerId == flowerId)
                    .SumAsync(t => (int?)t.ChangeQty, cancellationToken) ?? 0;

                return Math.Max(0, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "計算當前庫存時發生錯誤，花卉ID: {FlowerId}", flowerId);
                return 0;
            }
        }

        public async Task<double> CalculateWeeklyDemandAsync(
            int flowerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var since = DateTime.UtcNow.Date
                    .AddDays(-InventoryConstants.SafetyStock.DEMAND_LOOKBACK_DAYS);

                var transactions = await _context.Transactions
                    .AsNoTracking()
                    .Where(t => t.FlowerId == flowerId &&
                                t.TransactionType == TransactionType.Out &&
                                t.TransactionDate >= since)
                    .OrderBy(t => t.TransactionDate)
                    .ToListAsync(cancellationToken);

                if (!transactions.Any())
                    return GetDefaultWeeklyDemand(flowerId);

                var weeklyDemands = GroupTransactionsByWeek(transactions, since);

                if (weeklyDemands.Count >= InventoryConstants.SafetyStock.MIN_WEEKS_FOR_ANALYSIS)
                {
                    return CalculateRobustWeeklyDemand(weeklyDemands);
                }

                return GetDefaultWeeklyDemand(flowerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "計算每週需求時發生錯誤, 花卉ID: {FlowerId}", flowerId);

                return GetDefaultWeeklyDemand(flowerId);
            }
        }

        // =====================================================
        //  同步版本（為了向後兼容）
        // =====================================================

        [Obsolete("請使用非同步版本 CalculateEnhancedSafetyStockAsync")]
        public double CalculateEnhancedSafetyStock(Flower flower)
        {
            return CalculateEnhancedSafetyStockAsync(flower).GetAwaiter().GetResult();
        }

        [Obsolete("請使用非同步版本 CalculateCurrentStockAsync")]
        public int CalculateCurrentStock(Flower flower)
        {
            if (flower == null) return 0;
            return CalculateCurrentStockAsync(flower.Id).GetAwaiter().GetResult();
        }

        [Obsolete("請使用非同步版本 CalculateWeeklyDemandAsync")]
        public double CalculateWeeklyDemand(int flowerId)
        {
            return CalculateWeeklyDemandAsync(flowerId).GetAwaiter().GetResult();
        }

        // =====================================================
        //  私有輔助方法
        // =====================================================

        private async Task<EnhancedInventoryViewModel?> CreateInventoryViewModelAsync(
            Flower flower,
            CancellationToken cancellationToken)
        {
            try
            {
                var currentStock = await CalculateCurrentStockAsync(flower.Id, cancellationToken);
                var weeklyDemand = await CalculateWeeklyDemandAsync(flower.Id, cancellationToken);
                var safetyStock = await CalculateEnhancedSafetyStockAsync(flower, cancellationToken);
                var expiringBatches = await GetExpiringBatchesAsync(
                    flower.Id,
                    InventoryConstants.BusinessRules.EXPIRY_CHECK_DAYS,
                    cancellationToken);

                // ✅ 這裡補上 RecommendationLevel / Reason 的預設值
                var recommendation = new ReplenishmentRecommendation
                {
                    FlowerId = flower.Id,
                    FlowerName = flower.Name,
                    CurrentStock = currentStock,
                    SafetyStock = safetyStock,
                    WeeklyDemand = weeklyDemand,
                    Success = true,
                    RecommendedOrderDate = DateTime.UtcNow,
                    RecommendationLevel = InventoryConstants.ReplenishmentLevels.NONE,
                    Reason = "初始化"
                };

                if (currentStock < Math.Ceiling(safetyStock))
                {
                    recommendation.NeedReplenishment = true;
                    await CalculateReplenishmentDetailsAsync(
                        recommendation,
                        flower,
                        currentStock,
                        safetyStock,
                        weeklyDemand);
                }
                else
                {
                    recommendation.NeedReplenishment = false;
                    SetNoReplenishmentNeeded(recommendation);
                }

                return new EnhancedInventoryViewModel
                {
                    FlowerId = flower.Id,
                    Name = flower.Name,
                    Variety = flower.Variety ?? string.Empty,
                    Category = flower.Category,
                    ABCClass = flower.ABCClass,
                    CurrentStock = currentStock,
                    SafetyStock = safetyStock,
                    WeeklyDemand = weeklyDemand,
                    StockStatus = GetStockStatus(currentStock, safetyStock, weeklyDemand),
                    Recommendation = recommendation,
                    ExpiringBatches = expiringBatches,
                    InspectionPassRate = flower.InspectionPassRate,
                    Price = flower.Price
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "建立庫存視圖模型時發生錯誤 FlowerId={FlowerId}",
                    flower.Id);
                return null;
            }
        }


        private double CalculateSafetyStockInternal(
            Flower flower,
            double weeklyDemand,
            double sigma)
        {
            double serviceLevel = InventoryConstants.SafetyStock.DEFAULT_SERVICE_LEVEL;
            double Z = serviceLevel.GetZValue();

            double leadTimeDays = Math.Clamp(
                flower.LeadTimeDays,
                InventoryConstants.BusinessRules.MIN_LEAD_TIME_DAYS,
                InventoryConstants.BusinessRules.MAX_LEAD_TIME_DAYS);

            double leadTimeWeeks = Math.Max(1.0, leadTimeDays) / 7.0;

            // 安全庫存公式: Z × √(LT) × σ × 季節係數 ÷ 合格率
            double baseSafetyStock = Z * Math.Sqrt(leadTimeWeeks) * sigma;

            double seasonalFactor = Math.Clamp(
                (double)flower.SeasonalFactor,
                (double)InventoryConstants.BusinessRules.MIN_SEASONAL_FACTOR,
                (double)InventoryConstants.BusinessRules.MAX_SEASONAL_FACTOR);

            double passRate = Math.Clamp(
                (double)flower.InspectionPassRate,
                (double)InventoryConstants.BusinessRules.MIN_PASS_RATE,
                1.0);

            double adjustedSafetyStock = baseSafetyStock * seasonalFactor / passRate;

            return Math.Ceiling(Math.Max(1.0, adjustedSafetyStock));
        }

        private async Task<double> CalculateWeeklyDemandStdDevAsync(
            int flowerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var since = DateTime.UtcNow.Date
                    .AddDays(-InventoryConstants.SafetyStock.DEMAND_LOOKBACK_DAYS);

                var transactions = await _context.Transactions
                    .AsNoTracking()
                    .Where(t => t.FlowerId == flowerId &&
                                t.TransactionType == TransactionType.Out &&
                                t.TransactionDate >= since)
                    .ToListAsync(cancellationToken);

                if (!transactions.Any())
                    return GetDefaultStdDevFallback(flowerId);

                var weeklyDemands = GroupTransactionsByWeek(transactions, since);

                if (weeklyDemands.Count < InventoryConstants.SafetyStock.MIN_WEEKS_FOR_STD_DEV)
                {
                    return GetDefaultStdDevFallback(flowerId);
                }

                double mean = weeklyDemands.Average();
                double sumOfSquares = weeklyDemands.Sum(x => Math.Pow(x - mean, 2));
                double variance = sumOfSquares / (weeklyDemands.Count - 1);
                double stdDev = Math.Sqrt(variance);

                return Math.Max(stdDev, mean * 0.1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "計算需求標準差時發生錯誤, 花卉ID: {FlowerId}",
                    flowerId);

                return GetDefaultStdDevFallback(flowerId);
            }
        }

        private async Task CalculateReplenishmentDetailsAsync(
            ReplenishmentRecommendation recommendation,
            Flower flower,
            int currentStock,
            double safetyStock,
            double weeklyDemand)
        {
            double shortage = Math.Ceiling(safetyStock) - currentStock;
            double passRate = Math.Clamp((double)flower.InspectionPassRate, 0.01, 1.0);

            double suggestedOrder =
                (shortage / passRate) +
                (weeklyDemand * InventoryConstants.SafetyStock.BUFFER_WEEKS);

            recommendation.SuggestedOrderQuantity =
                (int)Math.Ceiling(Math.Max(1.0, suggestedOrder));

            recommendation.ExpectedPassQuantity =
                (int)Math.Floor(recommendation.SuggestedOrderQuantity * passRate);

            DetermineReplenishmentUrgency(
                recommendation,
                shortage,
                weeklyDemand,
                currentStock,
                safetyStock);

            await Task.CompletedTask;
        }

        private void DetermineReplenishmentUrgency(
            ReplenishmentRecommendation recommendation,
            double shortage,
            double weeklyDemand,
            int currentStock,
            double safetyStock)
        {
            var stockCoverageDays = weeklyDemand > 0
                ? (currentStock / weeklyDemand) * 7
                : 0;

            if (currentStock == 0)
            {
                recommendation.RecommendationLevel =
                    InventoryConstants.ReplenishmentLevels.CRITICAL;
                recommendation.Reason = "嚴重缺貨：當前庫存為零";
            }
            else if (shortage > weeklyDemand * 2 || stockCoverageDays < 3)
            {
                recommendation.RecommendationLevel =
                    InventoryConstants.ReplenishmentLevels.URGENT;
                recommendation.Reason =
                    $"緊急補貨：庫存僅能維持 {stockCoverageDays:F1} 天";
            }
            else if (shortage > weeklyDemand || stockCoverageDays < 7)
            {
                recommendation.RecommendationLevel =
                    InventoryConstants.ReplenishmentLevels.SUGGESTED;
                recommendation.Reason =
                    $"建議補貨：當前庫存 {currentStock} 低於安全庫存 {Math.Ceiling(safetyStock)}";
            }
            else
            {
                recommendation.RecommendationLevel =
                    InventoryConstants.ReplenishmentLevels.NONE;
                recommendation.Reason = "庫存充足";
            }
        }

        private void SetNoReplenishmentNeeded(
            ReplenishmentRecommendation recommendation)
        {
            recommendation.SuggestedOrderQuantity = 0;
            recommendation.ExpectedPassQuantity = 0;
            recommendation.RecommendationLevel =
                InventoryConstants.ReplenishmentLevels.NONE;
            recommendation.Reason = "庫存充足，無需補貨";
        }

        private ReplenishmentRecommendation CreateErrorRecommendation(
            int flowerId,
            string errorMessage)
        {
            return new ReplenishmentRecommendation
            {
                FlowerId = flowerId,
                FlowerName = "計算錯誤",
                RecommendationLevel =
                    InventoryConstants.ReplenishmentLevels.ERROR,
                Reason = errorMessage,
                Success = false
            };
        }

        private List<double> GroupTransactionsByWeek(
            List<Transaction> transactions,
            DateTime sinceDate)
        {
            var weeklySums = new Dictionary<int, double>();

            foreach (var transaction in transactions)
            {
                var weekNumber = GetWeekNumber(transaction.TransactionDate, sinceDate);
                weeklySums[weekNumber] =
                    weeklySums.GetValueOrDefault(weekNumber) +
                    Math.Abs(transaction.ChangeQty);
            }

            return weeklySums.Values.Where(sum => sum > 0).ToList();
        }

        private int GetWeekNumber(DateTime date, DateTime startDate)
        {
            return (int)((date.Date - startDate.Date).TotalDays / 7);
        }

        private double CalculateRobustWeeklyDemand(List<double> weeklyDemands)
        {
            if (!weeklyDemands.Any()) return 0;

            var sortedDemands = weeklyDemands.OrderBy(x => x).ToList();
            int count = sortedDemands.Count;

            double median = count % 2 == 0
                ? (sortedDemands[count / 2 - 1] + sortedDemands[count / 2]) / 2.0
                : sortedDemands[count / 2];

            int trimCount = Math.Max(1, count / 4);
            var trimmedDemands = sortedDemands
                .Skip(trimCount)
                .Take(count - 2 * trimCount);

            double trimmedMean = trimmedDemands.Any()
                ? trimmedDemands.Average()
                : median;

            return (median * 0.6 + trimmedMean * 0.4);
        }

        private async Task<List<Batch>> GetExpiringBatchesAsync(
            int flowerId,
            int daysThreshold,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var todayUtc = DateTime.UtcNow.Date;

                return await _context.Batches
                    .AsNoTracking()
                    .Where(b => b.FlowerId == flowerId &&
                                b.ExpiryDate.HasValue &&
                                b.ExpiryDate.Value >= todayUtc &&
                                b.ExpiryDate.Value <= todayUtc.AddDays(daysThreshold) &&
                                b.QuantityPassed > 0)
                    .OrderBy(b => b.ExpiryDate)
                    .ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "取得即將到期批次時發生錯誤，花卉ID: {FlowerId}", flowerId);
                return new List<Batch>();
            }
        }

        private string GetStockStatus(
            int currentStock,
            double safetyStock,
            double weeklyDemand)
        {
            if (currentStock == 0)
                return InventoryConstants.StockStatus.OUT_OF_STOCK;

            if (currentStock < Math.Ceiling(safetyStock))
                return InventoryConstants.StockStatus.LOW_STOCK;

            if (weeklyDemand > 0 && currentStock > weeklyDemand * 8)
                return InventoryConstants.StockStatus.OVERSTOCK;

            return InventoryConstants.StockStatus.NORMAL;
        }

        private double GetDefaultWeeklyDemand(int flowerId)
        {
            var flower = _context.Flowers.Find(flowerId);
            return flower?.ABCClass.GetDefaultDemandByABCClass() ?? 10.0;
        }

        private double GetDefaultStdDevFallback(int flowerId)
        {
            var avg = GetDefaultWeeklyDemand(flowerId);
            var flower = _context.Flowers.Find(flowerId);
            double cv = flower?.ABCClass.GetCVByABCClass()
                         ?? InventoryConstants.SafetyStock.DEFAULT_CV;
            return Math.Max(1.0, avg * cv);
        }
    }

    // =====================================================
    //  診斷 / 例外 類別
    // =====================================================

    public static class DiagnosticHelper
    {
        public static IDisposable StartActivity(string operationName)
        {
            return new DummyDisposable(operationName);
        }

        private class DummyDisposable : IDisposable
        {
            private readonly string _operationName;
            private readonly Stopwatch _stopwatch;

            public DummyDisposable(string operationName)
            {
                _operationName = operationName;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _stopwatch.Stop();
                // 可在這裡記錄執行時間 (_operationName, _stopwatch.Elapsed)
            }
        }
    }

    public class InventoryServiceException : Exception
    {
        public InventoryServiceException(string message)
            : base(message) { }

        public InventoryServiceException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
