using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlowerInventory.Models;
using FlowerInventory.Services;
using FlowerInventory.Utilities;

namespace FlowerInventory.Controllers
{
    public class FlowersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FlowersController> _logger;

        public FlowersController(ApplicationDbContext context, ILogger<FlowersController> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET: 花卉清單
        public async Task<IActionResult> Index()
        {
            try
            {
                var flowers = await _context.Flowers
                    .AsNoTracking()
                    .OrderBy(f => f.Name)
                    .ToListAsync();

                _logger.LogInformation("成功載入 {Count} 筆花卉清單", flowers.Count);
                return View(flowers);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入花卉清單");
                return View(new List<Flower>());
            }
        }

        // GET: 花卉詳情
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return this.RedirectWithError(nameof(Index), "未指定花卉ID");
            }

            try
            {
                var flower = await _context.Flowers
                    .Include(f => f.Batches.Where(b => b.Status == BatchStatus.Active))
                    .Include(f => f.Transactions.OrderByDescending(t => t.TransactionDate).Take(10))
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (flower == null)
                {
                    return this.RedirectWithError(nameof(Index), "找不到指定的花卉");
                }

                _logger.LogInformation("成功載入花卉詳情,ID: {FlowerId}", id);
                return View(flower);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入花卉詳情");
                return this.RedirectWithError(nameof(Index), "無法載入花卉詳情");
            }
        }

        // GET: 新增花卉
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: 新增花卉
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Flower flower)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("新增花卉時模組驗證失敗");
                    return View(flower);
                }

                // 業務邏輯驗證
                if (!IsValidFlowerForCreation(flower))
                {
                    ModelState.AddModelError("", "花卉資料驗證失敗，請檢查必填欄位");
                    return View(flower);
                }

                // 檢查名稱是否重複
                var existingFlower = await _context.Flowers
                    .AnyAsync(f => f.Name == flower.Name);

                if (existingFlower)
                {
                    ModelState.AddModelError("Name", "花卉名稱已存在，請使用其他名稱");
                    return View(flower);
                }

                flower.CreatedDate = DateTime.Now;
                _context.Add(flower);
                await _context.SaveChangesAsync();

                _logger.LogInformation("成功新增花卉, ID: {FlowerId}, 名稱: {FlowerName}", flower.Id, flower.Name);
                return this.RedirectWithSuccess(nameof(Index), $"花卉 {flower.Name} 新增成功！");
            }
            catch (DbUpdateException ex)
            {
                this.LogAndSetError(_logger, ex, "新增花卉");
                ModelState.AddModelError("", "儲存失敗，請檢查輸入資料");
                return View(flower);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "新增花卉");
                return View(flower);
            }
        }

        // GET: 編輯花卉
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return this.RedirectWithError(nameof(Index), "未指定花卉ID");
            }

            try
            {
                var flower = await _context.Flowers.FindAsync(id);
                if (flower == null)
                {
                    return this.RedirectWithError(nameof(Index), "找不到指定的花卉");
                }
                return View(flower);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入編輯花卉頁面");
                return this.RedirectWithError(nameof(Index), "無法載入編輯頁面");
            }
        }

        // POST: 編輯花卉
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Flower flower)
        {
            if (id != flower.Id)
            {
                _logger.LogWarning("編輯花卉時ID不匹配,請求ID: {RouteId}, 花卉ID: {FlowerId}", id, flower.Id);
                return this.RedirectWithError(nameof(Index), "花卉ID不匹配");
            }

            try
            {
                if (!ModelState.IsValid)
                {
                    return View(flower);
                }

                // 檢查名稱是否重複（排除自己）
                var existingFlower = await _context.Flowers
                    .AnyAsync(f => f.Name == flower.Name && f.Id != flower.Id);
                
                if (existingFlower)
                {
                    ModelState.AddModelError("Name", "花卉名稱已存在，請使用其他名稱");
                    return View(flower);
                }

                _context.Update(flower);
                await _context.SaveChangesAsync();

                _logger.LogInformation("成功更新花卉,ID: {FlowerId}, 名稱: {FlowerName}", flower.Id, flower.Name);
                return this.RedirectWithSuccess(nameof(Index), $"花卉 {flower.Name} 更新成功！");
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!FlowerExists(flower.Id))
                {
                    return this.RedirectWithError(nameof(Index), "花卉不存在或已被刪除");
                }
                else
                {
                    throw;
                }
            }
            catch (DbUpdateException ex)
            {
                this.LogAndSetError(_logger, ex, "更新花卉");
                ModelState.AddModelError("", "更新失敗，請檢查輸入資料");
                return View(flower);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "更新花卉");
                return View(flower);
            }
        }

        // GET: 刪除花卉
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return this.RedirectWithError(nameof(Index), "未指定花卉ID");
            }

            try
            {
                var flower = await _context.Flowers
                    .Include(f => f.Batches)
                    .Include(f => f.Transactions)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (flower == null)
                {
                    return this.RedirectWithError(nameof(Index), "找不到指定的花卉");
                }

                // 檢查是否可以刪除
                if (flower.Batches.Any() || flower.Transactions.Any())
                {
                    this.SetWarningMessage("此花卉已有相關的批次或交易紀錄,無法刪除");
                    return RedirectToAction(nameof(Index));
                }

                return View(flower);
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "載入刪除花卉頁面");
                return this.RedirectWithError(nameof(Index), "無法載入刪除頁面");
            }
        }

        // POST: 刪除花卉
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var flower = await _context.Flowers
                    .Include(f => f.Batches)
                    .Include(f => f.Transactions)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (flower == null)
                {
                    return this.RedirectWithError(nameof(Index), "花卉不存在或已被刪除");
                }

                // 檢查是否可以刪除
                if (flower.Batches.Any() || flower.Transactions.Any())
                {
                    return this.RedirectWithError(nameof(Index), "此花卉已有相關的批次或交易紀錄,無法刪除");
                }

                _context.Flowers.Remove(flower);
                await _context.SaveChangesAsync();

                _logger.LogInformation("成功刪除花卉,ID: {FlowerId}, 名稱: {FlowerName}", flower.Id, flower.Name);
                return this.RedirectWithSuccess(nameof(Index), $"花卉 {flower.Name} 刪除成功！");
            }
            catch (DbUpdateException ex)
            {
                this.LogAndSetError(_logger, ex, "刪除花卉");
                return this.RedirectWithError(nameof(Index), "刪除失敗,請稍後再試");
            }
            catch (Exception ex)
            {
                this.LogAndSetError(_logger, ex, "刪除花卉");
                return this.RedirectWithError(nameof(Index), "刪除花卉失敗");
            }
        }

        // API: 檢查花卉名稱是否存在
        [AcceptVerbs("GET", "POST")]
        public async Task<IActionResult> VerifyFlowerName(string name, int id = 0)
        {
            try
            {
                var exists = await _context.Flowers
                    .AnyAsync(f => f.Name == name && f.Id != id);
                
                return this.ApiSuccess(new { exists = !exists });
            }
            catch (Exception ex)
            {
                return this.ApiError(_logger, ex, "檢查花卉名稱");
            }
        }

        private bool FlowerExists(int id)
        {
            return _context.Flowers.Any(e => e.Id == id);
        }

        private bool IsValidFlowerForCreation(Flower flower)
        {
            return  !string.IsNullOrWhiteSpace(flower.Name) &&
                    !string.IsNullOrWhiteSpace(flower.Category) &&
                    !string.IsNullOrWhiteSpace(flower.ABCClass);
        }
    }    
}