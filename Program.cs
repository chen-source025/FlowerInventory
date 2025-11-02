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

app.UseStaticFiles();
app.UseHttpsRedirection();
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
        await Task.Delay(3000);

        var maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var canConnect = await context.Database.CanConnectAsync();

                if (canConnect)
                {
                    Console.WriteLine("✅ PostgreSQL 連線成功！");

                    // 檢查資料庫中現有的花卉數量
                    var flowerCount = await context.Flowers.CountAsync();
                    Console.WriteLine($"📊 當前資料庫中有 {flowerCount} 筆花卉資料");

                    // 確保資料庫存在
                    await context.Database.EnsureCreatedAsync();
                    Console.WriteLine("✅ 資料庫確保建立完成");

                    // 嘗試植入種子資料（只有當沒有資料時）
                    if (flowerCount == 0)
                    {
                        try
                        {
                            await context.SeedDataAsync();
                            var newCount = await context.Flowers.CountAsync();
                            Console.WriteLine($"🌱 種子資料植入完成，現在有 {newCount} 筆花卉資料");
                        }
                        catch (Exception seedEx)
                        {
                            Console.WriteLine($"⚠️ 種子資料植入警告: {seedEx.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("✅ 資料庫已有資料，跳過種子資料植入");
                    }

                    break; // 成功連接，跳出重試循環
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 連接嘗試 {i + 1}/{maxRetries} 失敗: {ex.Message}");
                if (i < maxRetries - 1)
                {
                    Console.WriteLine("🔄 等待 5 秒後重試...");
                    await Task.Delay(5000);
                }
                else
                {
                    Console.WriteLine("❌ 所有連接嘗試都失敗，但應用程式繼續啟動");
                }
            }
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