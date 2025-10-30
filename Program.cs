using Microsoft.EntityFrameworkCore;
using FlowerInventory.Models;
using FlowerInventory.Services;
using FlowerInventory.Middleware;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ========== 服務註冊 ==========
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    Console.WriteLine($"🔍 連線字串: {connectionString?.Substring(0, Math.Min(50, connectionString.Length))}...");
    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IEnhancedInventoryService, EnhancedInventoryService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ========== HTTP 請求管道 ==========
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ========== 資料庫初始化 ==========
try
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ApplicationDbContext>();

        Console.WriteLine("🔍 嘗試連接 PostgreSQL...");

        // 等待資料庫準備好
        await Task.Delay(2000);     // 給 Render 資料庫一些啟動時間
        
        var canConnect = await context.Database.CanConnectAsync();
        
        if (canConnect)
        {
            Console.WriteLine("✅ PostgreSQL 連線成功！");
            
            // 檢查遷移
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                Console.WriteLine($"🔄 執行遷移: {string.Join(", ", pendingMigrations)}");
                await context.Database.MigrateAsync();
                Console.WriteLine("✅ 資料庫遷移完成");
            }
            else
            {
                Console.WriteLine("✅ 無待處理遷移");
            }
            
            // 嘗試植入種子資料
            try 
            {
                await context.SeedDataAsync();
                Console.WriteLine("✅ 種子資料植入完成");
            }
            catch (Exception seedEx)
            {
                Console.WriteLine($"⚠️ 種子資料植入警告: {seedEx.Message}");
            }
        }
        else
        {
            Console.WriteLine("❌ 無法連接到資料庫");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ 資料庫初始化警告: {ex.Message}");
    Console.WriteLine("應用程式將繼續啟動，但資料庫功能可能受限");
}

// ========== 啟動應用程式 ==========
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"🚀 應用程式啟動在端口 {port}");
app.Run();
