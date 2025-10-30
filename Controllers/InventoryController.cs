using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlowerInventory.Models;
using FlowerInventory.Services;
using FlowerInventory.Utilities;

namespace FlowerInventory.Controllers
{
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEnhancedInventoryService _inventoryService;
        private readonly ILogger<InventoryController> _logger;

        public InventoryController(
            ApplicationDbContext context,
            IEnhancedInventoryService inventoryService,
            ILogger<InventoryController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET: 批次管理
        public async Task<IActionResult> Batches(CancellationToken cancellationToken = default)
        {
            try
            {
                var batches = await _context.Batches
                    .AsNoTracking()
                    .Include(b => b.Flower)
                    .OrderByDescending(b => b.ReceivedDate)
                    .ToListAsync(cancellationToken);

                var batchStatistics = CalculateBatchStatistics(batches);

                SetBatchViewBag(batchStatistics);
                _logger.LogInformation("成功載入批次管理頁面，共 {BatchCount} 筆批次", batches.Count);

                return View(batches);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入批次管理頁面");
                return View(new List<Batch>());
            }
        }

        // GET: 交易紀錄
        public async Task<IActionResult> Transactions(CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = await _context.Transactions
                    .AsNoTracking()
                    .Include(t => t.Flower)
                    .Include(t => t.Batch)
                    .OrderByDescending(t => t.TransactionDate)
                    .Take(1000)
                    .ToListAsync(cancellationToken);

                var transactionStats = CalculateTransactionStatistics(transactions);

                SetTransactionViewBag(transactionStats);
                _logger.LogInformation("成功載入交易紀錄頁面，共 {TransactionCount} 筆交易", transactions.Count);

                return View(transactions);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入交易紀錄頁面");
                return View(new List<Transaction>());
            }
        }

        // GET: 庫存報表
        public async Task<IActionResult> Report(CancellationToken cancellationToken = default)
        {
            try
            {
                var report = await _inventoryService.GenerateInventoryReportAsync(cancellationToken);

                SetReportViewBag(report);
                _logger.LogInformation("成功生成庫存報表，共 {FlowerCount} 種花卉", report.TotalFlowers);

                return View(report);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入庫存報表");
                return View(new InventoryReport());
            }
        }

        // GET: ABC 分析
        public async Task<IActionResult> ABCAnalysis(CancellationToken cancellationToken = default)
        {
            try
            {
                var report = await _inventoryService.GenerateABCReportAsync(cancellationToken);

                SetABCAnalysisViewBag(report);
                _logger.LogInformation("成功生成ABC分析報表，總價值: {TotalValue:C}", report.TotalValue);

                return View(report);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入ABC分析");
                return View(new ABCReport());
            }
        }

        // GET: 庫存價值分析
        public async Task<IActionResult> ValueAnalysis(CancellationToken cancellationToken = default)
        {
            try
            {
                var inventoryStatus = await _inventoryService.GetEnhancedInventoryStatusAsync(cancellationToken);
                var valueAnalysis = inventoryStatus
                    .OrderByDescending(i => i.TotalValue)
                    .ToList();

                var valueStats = CalculateValueStatistics(valueAnalysis);

                SetValueAnalysisViewBag(valueStats);
                _logger.LogInformation("成功載入庫存價值分析，總庫存價值: {TotalValue:C}", valueStats.TotalValue);

                return View(valueAnalysis);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入庫存價值分析");
                return View(new List<EnhancedInventoryViewModel>());
            }
        }

        // GET: 即將到期庫存
        public async Task<IActionResult> ExpiringInventory(CancellationToken cancellationToken = default)
        {
            try
            {
                var batches = await _context.Batches
                    .AsNoTracking()
                    .Include(b => b.Flower)
                    .Where(b => b.ExpiryDate.HasValue &&
                            b.ExpiryDate.Value <= DateTime.UtcNow.AddDays(7) &&
                            b.QuantityPassed > 0)
                    .OrderBy(b => b.ExpiryDate)
                    .ToListAsync(cancellationToken);

                var expiringItems = batches
                    .GroupBy(b => b.Flower)
                    .Select(g => new ExpiringInventoryViewModel
                    {
                        Flower = g.Key!,
                        ExpiringBatches = g.ToList(),
                        TotalExpiringQuantity = g.Sum(b => b.QuantityPassed),
                        EarliestExpiryDate = g.Min(b => b.ExpiryDate!.Value)
                    })
                    .OrderBy(e => e.EarliestExpiryDate)
                    .ToList();

                var expiringStats = CalculateExpiringStatistics(expiringItems);

                SetExpiringInventoryViewBag(expiringStats);
                _logger.LogInformation("成功載入即將到期庫存，共 {ItemCount} 項即將到期", expiringItems.Count);

                return View(expiringItems);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入即將到期庫存");
                return View(new List<ExpiringInventoryViewModel>());
            }
        }

        // GET: 庫存趨勢分析
        public async Task<IActionResult> TrendAnalysis(CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = await _context.Transactions
                    .AsNoTracking()
                    .Include(t => t.Flower)
                    .Where(t => t.TransactionDate >= DateTime.UtcNow.AddDays(-30))
                    .OrderBy(t => t.TransactionDate)
                    .ToListAsync(cancellationToken);

                var dailyTrends = transactions
                    .GroupBy(t => t.TransactionDate.Date)
                    .Select(g => new DailyTrendViewModel
                    {
                        Date = g.Key,
                        InboundQty = g.Where(t => t.IsInbound).Sum(t => t.ChangeQty),
                        OutboundQty = g.Where(t => t.IsOutbound).Sum(t => Math.Abs(t.ChangeQty)),
                        TransactionCount = g.Count()
                    })
                    .OrderBy(t => t.Date)
                    .ToList();

                var trendStats = CalculateTrendStatistics(dailyTrends);

                SetTrendAnalysisViewBag(trendStats);
                _logger.LogInformation("成功載入趨勢分析，分析期間: {Days} 天", dailyTrends.Count);

                return View(dailyTrends);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入趨勢分析");
                return View(new List<DailyTrendViewModel>());
            }
        }

        // API: 取得庫存數據 (用於圖表)
        [HttpGet]
        public async Task<IActionResult> GetInventoryChartData(CancellationToken cancellationToken = default)
        {
            try
            {
                var inventoryStatus = await _inventoryService.GetEnhancedInventoryStatusAsync(cancellationToken);

                var chartData = new
                {
                    categories = inventoryStatus.Select(i => i.Name).ToArray(),
                    currentStock = inventoryStatus.Select(i => i.CurrentStock).ToArray(),
                    safetyStock = inventoryStatus.Select(i => (int)Math.Ceiling(i.SafetyStock)).ToArray(),
                    values = inventoryStatus.Select(i => (double)i.TotalValue).ToArray(),
                    status = inventoryStatus.Select(i => i.StockStatus).ToArray()
                };

                _logger.LogDebug("成功生成庫存圖表數據，共 {Count} 項", inventoryStatus.Count);
                return this.ApiSuccess(chartData);
            }
            catch (Exception ex)
            {
                return this.ApiError(_logger, ex, "取得庫存圖表數據");
            }
        }

        // API: 取得交易趨勢數據
        [HttpGet]
        public async Task<IActionResult> GetTransactionTrendData(CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = await _context.Transactions
                    .AsNoTracking()
                    .Where(t => t.TransactionDate >= DateTime.UtcNow.AddDays(-30))
                    .OrderBy(t => t.TransactionDate)
                    .ToListAsync(cancellationToken);

                var dailyData = transactions
                    .GroupBy(t => t.TransactionDate.Date)
                    .Select(g => new
                    {
                        date = g.Key.ToString("MM/dd"),
                        inbound = g.Where(t => t.IsInbound).Sum(t => t.ChangeQty),
                        outbound = g.Where(t => t.IsOutbound).Sum(t => Math.Abs(t.ChangeQty)),
                        net = g.Where(t => t.IsInbound).Sum(t => t.ChangeQty) -
                            g.Where(t => t.IsOutbound).Sum(t => Math.Abs(t.ChangeQty))
                    })
                    .ToList();

                _logger.LogDebug("成功生成交易趨勢數據，共 {Days} 天資料", dailyData.Count);
                return this.ApiSuccess(dailyData);
            }
            catch (Exception ex)
            {
                return this.ApiError(_logger, ex, "取得交易趨勢數據");
            }
        }

        // API: 取得 ABC 分析數據
        [HttpGet]
        public async Task<IActionResult> GetABCAnalysisData(CancellationToken cancellationToken = default)
        {
            try
            {
                var report = await _inventoryService.GenerateABCReportAsync(cancellationToken);

                var abcData = new
                {
                    classes = new[] { "A", "B", "C" },
                    counts = new[] { report.ClassACount, report.ClassBCount, report.ClassCCount },
                    values = new[] { (double)report.ClassAValue, (double)report.ClassBValue, (double)report.ClassCValue },
                    percentages = new[] {
                        (double)report.ClassAPercentage,
                        (double)report.ClassBPercentage,
                        (double)report.ClassCPercentage
                    }
                };

                _logger.LogDebug("成功生成ABC分析數據");
                return this.ApiSuccess(abcData);
            }
            catch (Exception ex)
            {
                return this.ApiError(_logger, ex, "取得ABC分析數據");
            }
        }

        // 輔助方法
        private BatchStatistics CalculateBatchStatistics(List<Batch> batches)
        {
            return new BatchStatistics
            {
                TotalBatches = batches.Count,
                ExpiringSoon = batches.Count(b => b.IsExpiringSoon),
                Expired = batches.Count(b => b.IsExpired),
                AvgPassRate = batches.Any() ? batches.Average(b => b.PassRate) : 0,
                TotalReceived = batches.Sum(b => b.QuantityReceived),
                TotalPassed = batches.Sum(b => b.QuantityPassed)
            };
        }

        private TransactionStatistics CalculateTransactionStatistics(List<Transaction> transactions)
        {
            return new TransactionStatistics
            {
                TotalTransactions = transactions.Count,
                InboundCount = transactions.Count(t => t.IsInbound),
                OutboundCount = transactions.Count(t => t.IsOutbound),
                AdjustmentCount = transactions.Count(t => t.IsAdjustment),
                TotalInbound = transactions.Where(t => t.IsInbound).Sum(t => t.ChangeQty),
                TotalOutbound = transactions.Where(t => t.IsOutbound).Sum(t => Math.Abs(t.ChangeQty))
            };
        }

        private void SetBatchViewBag(BatchStatistics stats)
        {
            ViewBag.TotalBatches = stats.TotalBatches;
            ViewBag.ExpiringSoon = stats.ExpiringSoon;
            ViewBag.Expired = stats.Expired;
            ViewBag.AvgPassRate = stats.AvgPassRate;
            ViewBag.TotalReceived = stats.TotalReceived;
            ViewBag.TotalPassed = stats.TotalPassed;
        }

        private void SetTransactionViewBag(TransactionStatistics stats)
        {
            ViewBag.TotalTransactions = stats.TotalTransactions;
            ViewBag.InboundCount = stats.InboundCount;
            ViewBag.OutboundCount = stats.OutboundCount;
            ViewBag.AdjustmentCount = stats.AdjustmentCount;
            ViewBag.TotalInbound = stats.TotalInbound;
            ViewBag.TotalOutbound = stats.TotalOutbound;
        }

        private void SetReportViewBag(InventoryReport report)
        {
            ViewBag.TotalValue = report.TotalValue;
            ViewBag.TotalItems = report.TotalItems;
            ViewBag.LowStockCount = report.LowStockItems;
            ViewBag.OutOfStockCount = report.OutOfStockItems;
            ViewBag.ExpiringItems = report.ExpiringItems;
            ViewBag.ReplenishmentCount = report.ReplenishmentList.Count;
            ViewBag.ServiceLevel = report.ServiceLevel.ToString("P1");
        }

        private void SetABCAnalysisViewBag(ABCReport report)
        {
            ViewBag.TotalValue = report.TotalValue;
            ViewBag.ClassAPercentage = report.ClassAPercentage.ToString("F1");
            ViewBag.ClassBPercentage = report.ClassBPercentage.ToString("F1");
            ViewBag.ClassCPercentage = report.ClassCPercentage.ToString("F1");
        }

        private ValueStatistics CalculateValueStatistics(List<EnhancedInventoryViewModel> inventoryStatus)
        {
            return new ValueStatistics
            {
                TotalValue = inventoryStatus.Sum(i => i.TotalValue),
                AvgValue = inventoryStatus.Any() ? inventoryStatus.Average(i => i.TotalValue) : 0,
                MaxValue = inventoryStatus.Any() ? inventoryStatus.Max(i => i.TotalValue) : 0,
                MinValue = inventoryStatus.Any() ? inventoryStatus.Min(i => i.TotalValue) : 0,
                Top10Value = inventoryStatus.Take(10).Sum(i => i.TotalValue),
                Top10Percentage = inventoryStatus.Any() ?
                    (inventoryStatus.Take(10).Sum(i => i.TotalValue) / inventoryStatus.Sum(i => i.TotalValue) * 100) : 0
            };
        }

        private void SetValueAnalysisViewBag(ValueStatistics stats)
        {
            ViewBag.TotalValue = stats.TotalValue;
            ViewBag.AvgValue = stats.AvgValue;
            ViewBag.MaxValue = stats.MaxValue;
            ViewBag.MinValue = stats.MinValue;
            ViewBag.Top10Value = stats.Top10Value;
            ViewBag.Top10Percentage = stats.Top10Percentage;
        }

        private ExpiringStatistics CalculateExpiringStatistics(List<ExpiringInventoryViewModel> expiringItems)
        {
            return new ExpiringStatistics
            {
                TotalExpiringItems = expiringItems.Count,
                TotalExpiringQuantity = expiringItems.Sum(e => e.TotalExpiringQuantity),
                CriticalItems = expiringItems.Count(e => e.IsCritical),
                TotalValue = expiringItems.Sum(e => e.TotalValue)
            };
        }

        private void SetExpiringInventoryViewBag(ExpiringStatistics stats)
        {
            ViewBag.TotalExpiringItems = stats.TotalExpiringItems;
            ViewBag.TotalExpiringQuantity = stats.TotalExpiringQuantity;
            ViewBag.CriticalItems = stats.CriticalItems;
            ViewBag.TotalValue = stats.TotalValue;
        }

        private TrendStatistics CalculateTrendStatistics(List<DailyTrendViewModel> dailyTrends)
        {
            return new TrendStatistics
            {
                TotalInbound = dailyTrends.Sum(t => t.InboundQty),
                TotalOutbound = dailyTrends.Sum(t => t.OutboundQty),
                AvgDailyOutbound = dailyTrends.Any() ? dailyTrends.Average(t => t.OutboundQty) : 0,
                PeakOutboundDay = dailyTrends.Any() ?
                    dailyTrends.OrderByDescending(t => t.OutboundQty).First().DateDisplay : "N/A"
            };
        }

        private void SetTrendAnalysisViewBag(TrendStatistics stats)
        {
            ViewBag.TotalInbound = stats.TotalInbound;
            ViewBag.TotalOutbound = stats.TotalOutbound;
            ViewBag.AvgDailyOutbound = stats.AvgDailyOutbound;
            ViewBag.PeakOutboundDay = stats.PeakOutboundDay;
        }
    }

    // 統計數據類別
    public class BatchStatistics
    {
        public int TotalBatches { get; set; }
        public int ExpiringSoon { get; set; }
        public int Expired { get; set; }
        public decimal AvgPassRate { get; set; }
        public int TotalReceived { get; set; }
        public int TotalPassed { get; set; }
    }

    public class TransactionStatistics
    {
        public int TotalTransactions { get; set; }
        public int InboundCount { get; set; }
        public int OutboundCount { get; set; }
        public int AdjustmentCount { get; set; }
        public int TotalInbound { get; set; }
        public int TotalOutbound { get; set; }
    }

    public class ValueStatistics
    {
        public decimal TotalValue { get; set; }
        public decimal AvgValue { get; set; }
        public decimal MaxValue { get; set; }
        public decimal MinValue { get; set; }
        public decimal Top10Value { get; set; }
        public decimal Top10Percentage { get; set; }
    }

    public class ExpiringStatistics
    {
        public int TotalExpiringItems { get; set; }
        public int TotalExpiringQuantity { get; set; }
        public int CriticalItems { get; set; }
        public decimal TotalValue { get; set; }
    }

    public class TrendStatistics
    {
        public int TotalInbound { get; set; }
        public int TotalOutbound { get; set; }
        public double AvgDailyOutbound { get; set; }
        public string PeakOutboundDay { get; set; } = string.Empty;
    }

    // ViewModel 類別
    public class ExpiringInventoryViewModel
    {
        public required Flower Flower { get; set; }
        public List<Batch> ExpiringBatches { get; set; } = new();
        public int TotalExpiringQuantity { get; set; }
        public DateTime EarliestExpiryDate { get; set; }
        public bool IsCritical => EarliestExpiryDate <= DateTime.UtcNow.AddDays(3);
        public string Status => IsCritical ? "緊急" : "注意";

        public decimal TotalValue => TotalExpiringQuantity * (Flower.Price ?? 0);
    }

    public class DailyTrendViewModel
    {
        public DateTime Date { get; set; }
        public int InboundQty { get; set; }
        public int OutboundQty { get; set; }
        public int TransactionCount { get; set; }
        public int NetChange => InboundQty - OutboundQty;
        public string DateDisplay => Date.ToString("MM/dd");
        public string DayOfWeek => Date.ToString("ddd");
    }
}
