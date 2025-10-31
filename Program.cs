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
    Console.WriteLine($"🔍 連線字串: {connectionString?.Substring(0, Math.Min(50, connectionString?.Length ?? 0))}...");
    options.UseNpgsql(connectionString, 
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null));
});

builder.Services.AddScoped<IEnhancedInventoryService, EnhancedInventoryService>();
builder.Services.AddControllersWithViews();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();

var app = builder.Build();

// ========== HTTP 請求管道 ==========
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

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

        // 給資料庫一些啟動時間
        await Task.Delay(5000);
        
        var canConnect = await context.Database.CanConnectAsync();
        
        if (canConnect)
        {
            Console.WriteLine("✅ PostgreSQL 連線成功！");
            
            try
            {
                // 確保資料庫存在並執行遷移
                await context.Database.EnsureCreatedAsync();
                Console.WriteLine("✅ 資料庫確保建立完成");

                // 檢查是否有待處理的遷移
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
                    // 繼續執行，種子資料不是關鍵
                }
            }
            catch (Exception dbEx)
            {
                Console.WriteLine($"❌ 資料庫操作失敗: {dbEx.Message}");
                // 繼續啟動應用程式，可能是表格已經存在
            }
        }
        else
        {
            Console.WriteLine("❌ 無法連接到資料庫，但應用程式繼續啟動");
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