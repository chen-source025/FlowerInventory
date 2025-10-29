using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using FlowerInventory.Models;
using FlowerInventory.Utilities;
using Npgsql;

namespace FlowerInventory.Services
{
    public class EnhancedInventoryService : IEnhancedInventoryService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EnhancedInventoryService> _logger;
        private readonly TimeSpan _defaultCacheDuration = TimeSpan.FromMinutes(5);
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public EnhancedInventoryService(
            ApplicationDbContext context,
            ILogger<EnhancedInventoryService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ========== 公開 API ==========

        public async Task<List<EnhancedInventoryViewModel>> GetEnhancedInventoryStatusAsync(CancellationToken cancellationToken = default)
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
                    .Include(f => f.Batches.Where(b => b.Status == BatchStatus.Active))
                    .Include(f => f.Transactions.Where(t =>
                        t.TransactionDate >= DateTime.Now.AddDays(-InventoryConstants.SafetyStock.DEMAND_LOOKBACK_DAYS)))
                    .ToListAsync(cancellationToken);

                _logger.LogInformation("取得 {FlowerCount} 筆花卉資料進行庫存狀態計算", flowers.Count);

                // 使用並行處理但限制並發數
                var viewModels = new List<EnhancedInventoryViewModel>();
                var lockObject = new object();

                await Parallel.ForEachAsync(flowers, new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (flower, ct) =>
                {
                    try
                    {
                        var viewModel = await CreateInventoryViewModelAsync(flower, ct);
                        if (viewModel != null)
                        {
                            lock (lockObject)
                            {
                                viewModels.Add(viewModel);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "建立花卉 {FlowerId} 的庫存視圖模型時發生錯誤", flower.Id);
                    }
                });

                _logger.LogInformation("成功計算 {Count} 筆花卉庫存狀態", viewModels.Count);

                // 存入快取
                _cache.Set(cacheKey, viewModels, _defaultCacheDuration);

                return viewModels;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("庫存狀態查詢操作已被取消");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得庫存狀態時發生錯誤");
                throw new InventoryServiceException("取得庫存狀態失敗", ex);
            }
        }

        public async Task<ReplenishmentRecommendation> GetReplenishmentRecommendationAsync(int flowerId, CancellationToken cancellationToken = default)
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

                // 並行計算相關數據
                var currentStockTask = CalculateCurrentStockAsync(flowerId, cancellationToken);
                var safetyStockTask = CalculateEnhancedSafetyStockAsync(flower, cancellationToken);
                var weeklyDemandTask = CalculateWeeklyDemandAsync(flowerId, cancellationToken);

                await Task.WhenAll(currentStockTask, safetyStockTask, weeklyDemandTask);

                var currentStock = await currentStockTask;
                var safetyStock = await safetyStockTask;
                var weeklyDemand = await weeklyDemandTask;

                var recommendation = new ReplenishmentRecommendation
                {
                    FlowerId = flowerId,
                    FlowerName = flower.Name,
                    CurrentStock = currentStock,
                    SafetyStock = safetyStock,
                    WeeklyDemand = weeklyDemand,
                    NeedReplenishment = currentStock < Math.Ceiling(safetyStock),
                    Success = true,
                    RecommendedOrderDate = DateTime.Now,
                    RecommendationLevel = string.Empty,
                    Reason = string.Empty
                };

                if (recommendation.NeedReplenishment)
                {
                    await CalculateReplenishmentDetailsAsync(recommendation, flower, currentStock, safetyStock, weeklyDemand);
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

        public async Task<List<FlowerDemandAnalysis>> GetDemandAnalysisAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var flowers = await _context.Flowers.AsNoTracking().ToListAsync(cancellationToken);
                var analysisTasks = flowers.Select(async flower =>
                {
                    var weeklyDemandTask = CalculateWeeklyDemandAsync(flower.Id, cancellationToken);
                    var safetyStockTask = CalculateEnhancedSafetyStockAsync(flower, cancellationToken);
                    var stdDevTask = CalculateWeeklyDemandStdDevAsync(flower.Id, cancellationToken);

                    await Task.WhenAll(weeklyDemandTask, safetyStockTask, stdDevTask);

                    var weeklyDemand = await weeklyDemandTask;
                    var safetyStock = await safetyStockTask;
                    var stdDev = await stdDevTask;

                    // 使用擴充方法來判斷需求模式和變異性
                    var demandPattern = weeklyDemand.GetDemandPattern();
                    var cv = stdDev / Math.Max(weeklyDemand, 0.1);
                    var variabilityLevel = ConstantsExtensions.GetVariability(cv);

                    return new FlowerDemandAnalysis
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
                    };
                });

                var analysis = await Task.WhenAll(analysisTasks);
                return analysis.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得需求分析時發生錯誤");
                throw new InventoryServiceException("取得需求分析失敗", ex);
            }
        }  

        // ========== 庫存操作 ==========

        public async Task<InspectionResult> ProcessInspectionAsync(int batchId, int passedQuantity, string note, CancellationToken cancellationToken = default)
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
                batch.Status = BatchStatus.Inspected;

                // 計算品檢合格率並更新花卉資料
                var passRate = batch.QuantityReceived > 0 ? (decimal)passedQuantity / batch.QuantityReceived : 1m;
                batch.Flower.InspectionPassRate = passRate;

                // 建立庫存調整交易紀錄
                var adjustmentTransaction = new Transaction
                {
                    FlowerId = batch.FlowerId,
                    BatchId = batchId,
                    ChangeQty = passedQuantity,
                    TransactionType = TransactionType.In,
                    TransactionDate = DateTime.Now,
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
                    Message = $"{InventoryConstants.SuccessMessages.INSPECTION_COMPLETED}: {passedQuantity}/{batch.QuantityReceived} 合格 ({(passRate * 100):F1}%)",
                    Success = true,
                    InspectionDate = DateTime.Now,
                    BatchNumber = batch.BatchNo ?? string.Empty,
                    FlowerName = batch.Flower.Name
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "處理品檢作業時發生錯誤，批次ID: {BatchId}", batchId);
                return new InspectionResult
                {
                    BatchId = batchId,
                    ReceivedQty = 0,
                    PassedQty = 0,
                    PassRate = 0,
                    Message = $"{InventoryConstants.ErrorMessages.CALCULATION_ERROR}: {ex.Message}",
                    Success = false,
                    InspectionDate = DateTime.Now
                };
            }
        }

        public async Task<StockAdjustmentResult> AdjustStockAsync(int flowerId, int adjustmentQty, string reason, CancellationToken cancellationToken = default)
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

                // 建立庫存調整交易紀錄
                var transaction = new Transaction
                {
                    FlowerId = flowerId,
                    ChangeQty = adjustmentQty,
                    TransactionType = TransactionType.Adjust,
                    TransactionDate = DateTime.Now,
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
                    AdjustmentDate = DateTime.Now
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
                    AdjustmentDate = DateTime.Now
                };
            }
        }

        // ========== 報表生成 ==========

        public async Task<InventoryReport> GenerateInventoryReportAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var inventoryStatus = await GetEnhancedInventoryStatusAsync(cancellationToken);

                var replenishmentList = inventoryStatus
                    .Where(i => i.Recommendation?.NeedReplenishment == true)
                    .Select(i => i.Recommendation!)
                    .ToList();

                var criticalItems = inventoryStatus.Count(i =>
                    i.Recommendation?.RecommendationLevel == InventoryConstants.ReplenishmentLevels.CRITICAL);

                return new InventoryReport
                {
                    ReportDate = DateTime.Now,
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

        public async Task<ABCReport> GenerateABCReportAsync(CancellationToken cancellationToken = default)
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
                    ReportDate = DateTime.Now,
                    TotalValue = totalValue
                };

                foreach (var item in abcAnalysis)
                {
                    cumulative += item.TotalValue;
                    decimal percentage = totalValue > 0 ? (item.TotalValue / totalValue) * 100 : 0;
                    decimal cumulativePercentage = totalValue > 0 ? (cumulative / totalValue) * 100 : 0;

                    string abcClass = cumulativePercentage switch
                    {
                        <= InventoryConstants.ABCClassification.CLASS_A_THRESHOLD => InventoryConstants.ABCClasses.CLASS_A,
                        <= InventoryConstants.ABCClassification.CLASS_B_THRESHOLD => InventoryConstants.ABCClasses.CLASS_B,
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

        // ========== 計算方法 ==========

        public async Task<double> CalculateEnhancedSafetyStockAsync(Flower flower, CancellationToken cancellationToken = default)
        {
            if (flower == null)
                throw new ArgumentNullException(nameof(flower));

            try
            {
                // 並行計算相關數據
                var weeklyDemandTask = CalculateWeeklyDemandAsync(flower.Id, cancellationToken);
                var stdDevTask = CalculateWeeklyDemandStdDevAsync(flower.Id, cancellationToken);

                await Task.WhenAll(weeklyDemandTask, stdDevTask);

                var weeklyDemand = await weeklyDemandTask;
                var sigma = await stdDevTask;

                return CalculateSafetyStockInternal(flower, weeklyDemand, sigma);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "計算安全庫存錯誤 FlowerId={FlowerId}, Name={FlowerName}", flower.Id, flower.Name);
                return flower.ABCClass.GetDefaultDemandByABCClass();
            }
        }

        public async Task<int> CalculateCurrentStockAsync(int flowerId, CancellationToken cancellationToken = default)
        {
            try
            {
                var totalPassed = await _context.Batches
                    .Where(b => b.FlowerId == flowerId &&
                            b.Status == BatchStatus.Active)
                    .SumAsync(b => b.QuantityPassed, cancellationToken);

                var totalSold = await _context.Transactions
                    .Where(t => t.FlowerId == flowerId &&
                            t.TransactionType == TransactionType.Out)
                    .SumAsync(t => (int?)Math.Abs(t.ChangeQty), cancellationToken) ?? 0;

                return Math.Max(0, totalPassed - totalSold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "計算當前庫存時發生錯誤，花卉ID: {FlowerId}", flowerId);
                return 0;
            }
        }

        public async Task<double> CalculateWeeklyDemandAsync(int flowerId, CancellationToken cancellationToken = default)
        {
            try
            {
                var since = DateTime.Now.Date.AddDays(-InventoryConstants.SafetyStock.DEMAND_LOOKBACK_DAYS);

                var transactions = await _context.Transactions
                    .AsNoTracking()
                    .Where(t => t.FlowerId == flowerId &&
                               t.TransactionType == TransactionType.Out &&
                               t.TransactionDate >= since)
                    .OrderBy(t => t.TransactionDate)
                    .ToListAsync(cancellationToken);

                if (!transactions.Any())
                    return GetDefaultWeeklyDemand(flowerId);

                // 分週計算需求
                var weeklyDemands = GroupTransactionsByWeek(transactions, since);

                // 需要足夠的數據點才使用實際計算
                if (weeklyDemands.Count >= InventoryConstants.SafetyStock.MIN_WEEKS_FOR_ANALYSIS)
                {
                    return CalculateRobustWeeklyDemand(weeklyDemands);
                }

                return GetDefaultWeeklyDemand(flowerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "計算每週需求時發生錯誤，花卉ID: {FlowerId}", flowerId);
                return GetDefaultWeeklyDemand(flowerId);
            }
        }

        // ========== 同步版本（為了向後兼容） ==========

        [System.Obsolete("請使用非同步版本 CalculateEnhancedSafetyStockAsync")]
        public double CalculateEnhancedSafetyStock(Flower flower)
        {
            return CalculateEnhancedSafetyStockAsync(flower).GetAwaiter().GetResult();
        }

        [System.Obsolete("請使用非同步版本 CalculateCurrentStockAsync")]
        public int CalculateCurrentStock(Flower flower)
        {
            if (flower == null) return 0;
            return CalculateCurrentStockAsync(flower.Id).GetAwaiter().GetResult();
        }

        [System.Obsolete("請使用非同步版本 CalculateWeeklyDemandAsync")]
        public double CalculateWeeklyDemand(int flowerId)
        {
            return CalculateWeeklyDemandAsync(flowerId).GetAwaiter().GetResult();
        }

        // ========== 私有輔助方法 ==========

        private async Task<EnhancedInventoryViewModel> CreateInventoryViewModelAsync(Flower flower, CancellationToken cancellationToken)
        {
            try
            {
                // 並行計算所有必要數據
                var currentStockTask = CalculateCurrentStockAsync(flower.Id, cancellationToken);
                var safetyStockTask = CalculateEnhancedSafetyStockAsync(flower, cancellationToken);
                var weeklyDemandTask = CalculateWeeklyDemandAsync(flower.Id, cancellationToken);
                var recommendationTask = GetReplenishmentRecommendationAsync(flower.Id, cancellationToken);
                var expiringBatchesTask = GetExpiringBatchesAsync(flower.Id, InventoryConstants.BusinessRules.EXPIRY_CHECK_DAYS, cancellationToken);

                await Task.WhenAll(currentStockTask, safetyStockTask, weeklyDemandTask, recommendationTask, expiringBatchesTask);

                var currentStock = await currentStockTask;
                var safetyStock = await safetyStockTask;
                var weeklyDemand = await weeklyDemandTask;

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
                    Recommendation = await recommendationTask,
                    ExpiringBatches = await expiringBatchesTask,
                    InspectionPassRate = flower.InspectionPassRate,
                    Price = flower.Price
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "建立庫存視圖模型時發生錯誤 FlowerId={FlowerId}", flower.Id);
                return null!;
            }
        }

        private double CalculateSafetyStockInternal(Flower flower, double weeklyDemand, double sigma)
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

        private async Task<double> CalculateWeeklyDemandStdDevAsync(int flowerId, CancellationToken cancellationToken = default)
        {
            try
            {
                var since = DateTime.Now.Date.AddDays(-InventoryConstants.SafetyStock.DEMAND_LOOKBACK_DAYS);

                var transactions = await _context.Transactions
                    .AsNoTracking()
                    .Where(t => t.FlowerId == flowerId &&
                            t.TransactionType == TransactionType.Out &&
                            t.TransactionDate >= since)
                    .ToListAsync(cancellationToken);

                if (!transactions.Any())
                    return GetDefaultStdDevFallback(flowerId);

                var weeklyDemands = GroupTransactionsByWeek(transactions, since);

                // 需要足夠的數據點來計算標準差
                if (weeklyDemands.Count < InventoryConstants.SafetyStock.MIN_WEEKS_FOR_STD_DEV)
                {
                    return GetDefaultStdDevFallback(flowerId);
                }

                // 計算樣本標準差 (n-1)
                double mean = weeklyDemands.Average();
                double sumOfSquares = weeklyDemands.Sum(x => Math.Pow(x - mean, 2));
                double variance = sumOfSquares / (weeklyDemands.Count - 1);
                double stdDev = Math.Sqrt(variance);

                // 保護性最小標準差
                return Math.Max(stdDev, mean * 0.1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "計算需求標準差時發生錯誤，花卉ID: {FlowerId}", flowerId);
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

            // 計算建議訂購量：考慮缺貨、緩衝和合格率
            double suggestedOrder = (shortage / passRate) +
                                  (weeklyDemand * InventoryConstants.SafetyStock.BUFFER_WEEKS);

            recommendation.SuggestedOrderQuantity = (int)Math.Ceiling(Math.Max(1.0, suggestedOrder));
            recommendation.ExpectedPassQuantity = (int)Math.Floor(recommendation.SuggestedOrderQuantity * passRate);

            // 決定補貨緊急程度
            DetermineReplenishmentUrgency(recommendation, shortage, weeklyDemand, currentStock, safetyStock);
        }

        private void DetermineReplenishmentUrgency(
            ReplenishmentRecommendation recommendation,
            double shortage,
            double weeklyDemand,
            int currentStock,
            double safetyStock)
        {
            var stockCoverageDays = weeklyDemand > 0 ? (currentStock / weeklyDemand) * 7 : 0;

            if (currentStock == 0)
            {
                recommendation.RecommendationLevel = InventoryConstants.ReplenishmentLevels.CRITICAL;
                recommendation.Reason = "嚴重缺貨：當前庫存為零";
            }
            else if (shortage > weeklyDemand * 2 || stockCoverageDays < 3)
            {
                recommendation.RecommendationLevel = InventoryConstants.ReplenishmentLevels.URGENT;
                recommendation.Reason = $"緊急補貨：庫存僅能維持 {stockCoverageDays:F1} 天";
            }
            else if (shortage > weeklyDemand || stockCoverageDays < 7)
            {
                recommendation.RecommendationLevel = InventoryConstants.ReplenishmentLevels.SUGGESTED;
                recommendation.Reason = $"建議補貨：當前庫存 {currentStock} 低於安全庫存 {Math.Ceiling(safetyStock)}";
            }
            else
            {
                recommendation.RecommendationLevel = InventoryConstants.ReplenishmentLevels.NONE;
                recommendation.Reason = "庫存充足";
            }
        }

        private void SetNoReplenishmentNeeded(ReplenishmentRecommendation recommendation)
        {
            recommendation.SuggestedOrderQuantity = 0;
            recommendation.ExpectedPassQuantity = 0;
            recommendation.RecommendationLevel = InventoryConstants.ReplenishmentLevels.NONE;
            recommendation.Reason = "庫存充足，無需補貨";
        }

        private ReplenishmentRecommendation CreateErrorRecommendation(int flowerId, string errorMessage)
        {
            return new ReplenishmentRecommendation
            {
                FlowerId = flowerId,
                FlowerName = "計算錯誤",
                RecommendationLevel = InventoryConstants.ReplenishmentLevels.ERROR,
                Reason = errorMessage,
                Success = false
            };
        }

        private List<double> GroupTransactionsByWeek(List<Transaction> transactions, DateTime sinceDate)
        {
            var weeklySums = new Dictionary<int, double>();

            foreach (var transaction in transactions)
            {
                var weekNumber = GetWeekNumber(transaction.TransactionDate, sinceDate);
                weeklySums[weekNumber] = weeklySums.GetValueOrDefault(weekNumber) + Math.Abs(transaction.ChangeQty);
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

            // 計算中位數
            double median = count % 2 == 0
                ? (sortedDemands[count / 2 - 1] + sortedDemands[count / 2]) / 2.0
                : sortedDemands[count / 2];

            // 修剪極端值（去除最高和最低的25%）
            int trimCount = Math.Max(1, count / 4);
            var trimmedDemands = sortedDemands
                .Skip(trimCount)
                .Take(count - 2 * trimCount);

            double trimmedMean = trimmedDemands.Any() ? trimmedDemands.Average() : median;

            // 返回中位數和修剪平均數的加權平均，偏向中位數
            return (median * 0.6 + trimmedMean * 0.4);
        }

        private async Task<List<Batch>> GetExpiringBatchesAsync(int flowerId, int daysThreshold, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _context.Batches
                    .AsNoTracking()
                    .Where(b => b.FlowerId == flowerId &&
                            b.ExpiryDate.HasValue &&
                            b.ExpiryDate.Value <= DateTime.Now.AddDays(daysThreshold) &&
                            b.QuantityPassed > 0)
                    .OrderBy(b => b.ExpiryDate)
                    .ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得即將到期批次時發生錯誤，花卉ID: {FlowerId}", flowerId);
                return new List<Batch>();
            }
        }

        private string GetStockStatus(int currentStock, double safetyStock, double weeklyDemand)
        {
            if (currentStock == 0)
                return InventoryConstants.StockStatus.OUT_OF_STOCK;

            if (currentStock < Math.Ceiling(safetyStock))
                return InventoryConstants.StockStatus.LOW_STOCK;

            if (currentStock > weeklyDemand * 8) // 超過8週需求視為過量
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
            double cv = flower?.ABCClass.GetCVByABCClass() ?? InventoryConstants.SafetyStock.DEFAULT_CV;
            return Math.Max(1.0, avg * cv);
        }
    }

    // 診斷輔助類別
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
                // 可以在這裡記錄執行時間
            }
        }
    }

    public class InventoryServiceException : Exception
    {
        public InventoryServiceException(string message) : base(message) { }
        public InventoryServiceException(string message, Exception innerException) : base(message, innerException) { }
    }
}
