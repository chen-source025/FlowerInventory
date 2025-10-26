using Microsoft.EntityFrameworkCore;
using FlowerInventory.Models;
using FlowerInventory.Services;
using FlowerInventory.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ========== 服務註冊 ==========

// 加入 DbContext 服務
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 加入自訂服務
builder.Services.AddScoped<IEnhancedInventoryService, EnhancedInventoryService>();

// 加入控制器和視圖服務
builder.Services.AddControllersWithViews();

// 開發環境專用服務
if (builder.Environment.IsDevelopment())
{
    // EF Core 錯誤頁面（開發環境）
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
    
    // Razor 即時編譯（修改 .cshtml 不需重啟）
    builder.Services.AddRazorPages().AddRazorRuntimeCompilation();
}

var app = builder.Build();

// ========== HTTP 請求管道 ==========

// 使用全域例外處理中介軟體
app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // 開發環境：顯示詳細錯誤
    app.UseDeveloperExceptionPage();

    // 提供網頁介面執行資料庫遷移
    app.UseMigrationsEndPoint();
}
else
{
    // 正式環境：顯示友善錯誤頁面
    app.UseExceptionHandler("/Home/Error");

    // 啟用 HTTP Strict Transport Security
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// 設定預設路由
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ========== 資料庫初始化 ==========

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated();
        
        Console.WriteLine("✅ 資料庫初始化完成！");
        
        // 可在這裡執行種子資料
        // await SeedData.InitializeAsync(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "❌ 資料庫建立或初始化時發生錯誤");
    }
}

app.Run();
// ========== 結束 ==========