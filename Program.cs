using Microsoft.EntityFrameworkCore;
using FlowerInventory.Models;
using FlowerInventory.Services;
using FlowerInventory.Middleware;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ========== 服務註冊 ==========

// 加入 DbContext 服務 - 確保使用 PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    app.UseMigrationsEndPoint();
}
else
{
    // 正式環境：顯示友善錯誤頁面
    app.UseExceptionHandler("/Home/Error");
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

        // 重試機制
        var retryCount = 0;
        const int maxRetries = 5;

        while (retryCount < maxRetries)
        {
            try
            {
                Console.WriteLine($"🔍 檢查 PostgreSQL 連線... (嘗試 {retryCount + 1}/{maxRetries})");
                var canConnect = await context.Database.CanConnectAsync();

                if (canConnect)
                {
                    Console.WriteLine("✅ PostgreSQL 連線成功！");

                    // 確保資料庫建立並執行遷移
                    Console.WriteLine("🔄 執行資料庫遷移...");
                    await context.Database.MigrateAsync();

                    // 植入種子資料
                    Console.WriteLine("🌱 植入種子資料...");
                    await context.SeedDataAsync();

                    Console.WriteLine("✅ PostgreSQL 資料庫初始化完成！");
                    break;
                }
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                Console.WriteLine($"❌ 連線失敗，{ex.Message}，等待 5 秒後重試...");
                await Task.Delay(5000);
            }
        }

        if (retryCount == maxRetries)
        {
            Console.WriteLine("❌ 無法連接到 PostgreSQL 資料庫，請檢查連線設定");
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "❌ 資料庫建立或初始化時發生錯誤");
        Console.WriteLine($"❌ 資料庫錯誤: {ex.Message}");
    }
}

// ========== 添加端口綁定 ==========
// 解決 Render 部署的問題
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

// ========== 啟動應用程式 ==========
app.Run();
