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
// builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();

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
        await Task.Delay(3000); // 給資料庫一些啟動時間

        var maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var canConnect = await context.Database.CanConnectAsync();
                if (canConnect)
                {
                    Console.WriteLine("✅ PostgreSQL 連線成功！");

                    await context.Database.MigrateAsync();
                    Console.WriteLine("✅ 資料庫遷移完成");

                    // 對齊 Flowers.Id 的序列
                    // 對齊 Batches.Id 的序列
                    // 對齊 Transactions.Id 的序列
                    await context.Database.ExecuteSqlRawAsync(@"
                        SELECT setval(
                        pg_get_serial_sequence('""Flowers""', 'Id'),
                        COALESCE(MAX(""Id""), 1)
                        )
                        FROM ""Flowers"";
                        SELECT setval(
                        pg_get_serial_sequence('""Batches""', 'Id'),
                        COALESCE(MAX(""Id""), 1)
                        )
                        FROM ""Batches"";
                        SELECT setval(
                        pg_get_serial_sequence('""Transactions""', 'Id'),
                        COALESCE(MAX(""Id""), 1)
                        )
                        FROM ""Transactions"";
                    ");
                    // 檢查現有資料
                    var flowerCount = await context.Flowers.CountAsync();
                    Console.WriteLine($"📊 當前資料庫中有 {flowerCount} 筆花卉資料");

                    // 植入種子資料 (只當沒有資料時)
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
                    break;
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
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ 資料庫初始化警告: {ex.Message}");
}

// ========== 啟動應用程式 ==========
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"🚀 應用程式啟動在端口 {port}");
app.Run();