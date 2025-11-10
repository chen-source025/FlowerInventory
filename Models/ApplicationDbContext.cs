using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace FlowerInventory.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Flower> Flowers { get; set; } = null!;
        public DbSet<Batch> Batches { get; set; } = null!;
        public DbSet<Transaction> Transactions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Flower>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();

                // 為 decimal? 類型使用正確的預設值類型
                entity.Property(e => e.Price)
                    .HasColumnType("decimal(10,2)")
                    .HasDefaultValue(0m);

                entity.Property(e => e.SeasonalFactor)
                    .HasColumnType("decimal(4,2)")
                    .HasDefaultValue(1.0m);

                entity.Property(e => e.InspectionPassRate)
                    .HasColumnType("decimal(3,2)")
                    .HasDefaultValue(0.8m);

                // 設定非空約束
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ABCClass).IsRequired().HasMaxLength(1);
                entity.Property(e => e.ShelfLifeDays).IsRequired().HasDefaultValue(30);
                entity.Property(e => e.LeadTimeDays).HasDefaultValue(7);
                entity.Property(e => e.SupplierDeliveriesPerWeek).HasDefaultValue(1);
                entity.Property(e => e.ReplenishCycleWeeks).HasDefaultValue(1);
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("NOW()");
            });

            // Batch 模型配置
            modelBuilder.Entity<Batch>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();

                entity.Property(e => e.BatchNo).HasMaxLength(50);
                entity.Property(e => e.InspectionNote).HasMaxLength(500);
                entity.Property(e => e.Status).HasDefaultValue(BatchStatus.Active);
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("NOW()");
                entity.Property(e => e.ReceivedDate).HasDefaultValueSql("NOW()");

                // 外鍵關係
                entity.HasOne(b => b.Flower)
                    .WithMany(f => f.Batches)
                    .HasForeignKey(b => b.FlowerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Transaction 模型配置
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();

                entity.Property(e => e.Note).HasMaxLength(400);
                entity.Property(e => e.TransactionType).HasDefaultValue(TransactionType.In);
                entity.Property(e => e.TransactionDate).HasDefaultValueSql("NOW()");

                // 外鍵關係
                entity.HasOne(t => t.Flower)
                    .WithMany(f => f.Transactions)
                    .HasForeignKey(t => t.FlowerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(t => t.Batch)
                    .WithMany()
                    .HasForeignKey(t => t.BatchId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // 索引配置
            modelBuilder.Entity<Flower>()
                .HasIndex(f => f.Name)
                .IsUnique();

            modelBuilder.Entity<Flower>()
                .HasIndex(f => f.Category);

            modelBuilder.Entity<Flower>()
                .HasIndex(f => f.ABCClass);

            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.BatchNo)
                .IsUnique();

            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.ReceivedDate);

            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.ExpiryDate);

            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.Status);

            modelBuilder.Entity<Transaction>()
                .HasIndex(t => t.TransactionDate);

            modelBuilder.Entity<Transaction>()
                .HasIndex(t => t.TransactionType);

            modelBuilder.Entity<Transaction>()
                .HasIndex(t => new { t.FlowerId, t.TransactionDate });
        }

        public async Task SeedDataAsync()
        {
            // 只有當沒有資料時才植入
            if (!Flowers.Any())
            {
                await Flowers.AddRangeAsync(GetFlowerSeedData());
                await SaveChangesAsync();
            }

            if (!Batches.Any())
            {
                await Batches.AddRangeAsync(GetBatchSeedData());
                await SaveChangesAsync();
            }

            if (!Transactions.Any())
            {
                await Transactions.AddRangeAsync(GetTransactionSeedData());
                await SaveChangesAsync();
            }
        }

        private static List<Flower> GetFlowerSeedData()
        {
            return new List<Flower>
            {
                // 常駐型 (2種)
                new Flower
                {
                    Id = 1,
                    Name = "黃金葛",
                    Variety = "普通",
                    Category = "常駐型",
                    ABCClass = "C",
                    ShelfLifeDays = 30,
                    Price = 80,
                    SeasonalFactor = 1.0m,
                    InspectionPassRate = 0.95m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 2,
                    Name = "火鶴",
                    Variety = "紅火鶴",
                    Category = "常駐型",
                    ABCClass = "A",
                    ShelfLifeDays = 14,
                    Price = 120,
                    SeasonalFactor = 1.0m,
                    InspectionPassRate = 0.85m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },

                // 宗教型 (4種)
                new Flower
                {
                    Id = 3,
                    Name = "蒼蘭",
                    Variety = "普通",
                    Category = "宗教型",
                    ABCClass = "B",
                    ShelfLifeDays = 10,
                    Price = 60,
                    SeasonalFactor = 1.2m,
                    InspectionPassRate = 0.80m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 4,
                    Name = "茱萸",
                    Variety = "普通",
                    Category = "宗教型",
                    ABCClass = "B",
                    ShelfLifeDays = 12,
                    Price = 70,
                    SeasonalFactor = 1.3m,
                    InspectionPassRate = 0.82m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 5,
                    Name = "抹草",
                    Variety = "普通",
                    Category = "宗教型",
                    ABCClass = "C",
                    ShelfLifeDays = 15,
                    Price = 40,
                    SeasonalFactor = 1.1m,
                    InspectionPassRate = 0.90m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 6,
                    Name = "芙蓉",
                    Variety = "普通",
                    Category = "宗教型",
                    ABCClass = "B",
                    ShelfLifeDays = 8,
                    Price = 55,
                    SeasonalFactor = 1.2m,
                    InspectionPassRate = 0.78m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },

                // 季節性 (3種)
                new Flower
                {
                    Id = 7,
                    Name = "紫薇",
                    Variety = "普通",
                    Category = "季節性",
                    ABCClass = "B",
                    ShelfLifeDays = 9,
                    Price = 65,
                    SeasonalFactor = 1.5m,
                    InspectionPassRate = 0.75m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 8,
                    Name = "菊花",
                    Variety = "黃菊",
                    Category = "季節性",
                    ABCClass = "B",
                    ShelfLifeDays = 11,
                    Price = 58,
                    SeasonalFactor = 1.4m,
                    InspectionPassRate = 0.80m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 9,
                    Name = "金桔",
                    Variety = "盆栽",
                    Category = "季節性",
                    ABCClass = "A",
                    ShelfLifeDays = 20,
                    Price = 150,
                    SeasonalFactor = 1.6m,
                    InspectionPassRate = 0.70m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },

                // 流行性 (5種)
                new Flower
                {
                    Id = 10,
                    Name = "多肉植物",
                    Variety = "混合",
                    Category = "流行性",
                    ABCClass = "C",
                    ShelfLifeDays = 25,
                    Price = 90,
                    SeasonalFactor = 1.0m,
                    InspectionPassRate = 0.95m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 11,
                    Name = "塊根植物",
                    Variety = "混合",
                    Category = "流行性",
                    ABCClass = "C",
                    ShelfLifeDays = 28,
                    Price = 110,
                    SeasonalFactor = 1.0m,
                    InspectionPassRate = 0.92m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 12,
                    Name = "蝶豆花",
                    Variety = "藍色",
                    Category = "流行性",
                    ABCClass = "A",
                    ShelfLifeDays = 7,
                    Price = 85,
                    SeasonalFactor = 1.2m,
                    InspectionPassRate = 0.75m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 13,
                    Name = "石斛蘭",
                    Variety = "蝴蝶蘭",
                    Category = "流行性",
                    ABCClass = "A",
                    ShelfLifeDays = 12,
                    Price = 180,
                    SeasonalFactor = 1.8m,
                    InspectionPassRate = 0.70m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
                new Flower
                {
                    Id = 14,
                    Name = "綠籬植物",
                    Variety = "常綠",
                    Category = "流行性",
                    ABCClass = "C",
                    ShelfLifeDays = 30,
                    Price = 75,
                    SeasonalFactor = 1.0m,
                    InspectionPassRate = 0.98m,
                    LeadTimeDays = 7,
                    CreatedDate = DateTime.UtcNow
                },
            };
        }

        private static List<Batch> GetBatchSeedData()
        {
            // 設定批次種子資料 - 選擇代表性花卉
            return new List<Batch>
            {
                new Batch
                {
                    Id = 1,
                    FlowerId = 1,
                    BatchNo = "GG2024001",
                    QuantityReceived = 50,
                    QuantityPassed = 48,
                    ReceivedDate = DateTime.UtcNow.AddDays(-5),
                    ExpiryDate = DateTime.UtcNow.AddDays(25),
                    InspectionNote = "品檢良好,僅2株葉片受損",
                    Status = BatchStatus.Active,
                    CreatedDate = DateTime.UtcNow.AddDays(-5)
                },
                new Batch
                {
                    Id = 2,
                    FlowerId = 2,
                    BatchNo = "AN2024001",
                    QuantityReceived = 30,
                    QuantityPassed = 25,
                    ReceivedDate = DateTime.UtcNow.AddDays(-3),
                    ExpiryDate = DateTime.UtcNow.AddDays(11),
                    InspectionNote = "5株花瓣有瑕疵",
                    Status = BatchStatus.Active,
                    CreatedDate = DateTime.UtcNow.AddDays(-3)
                },
                new Batch
                {
                    Id = 3,
                    FlowerId = 13,
                    BatchNo = "DE2024001",
                    QuantityReceived = 20,
                    QuantityPassed = 14,
                    ReceivedDate = DateTime.UtcNow.AddDays(-1),
                    ExpiryDate = DateTime.UtcNow.AddDays(11),
                    InspectionNote = "蘭花較為脆弱,6株運輸受損",
                    Status = BatchStatus.Active,
                    CreatedDate = DateTime.UtcNow.AddDays(-1)
                },
                new Batch
                {
                    Id = 4,
                    FlowerId = 7,
                    BatchNo = "LV2024001",
                    QuantityReceived = 40,
                    QuantityPassed = 32,
                    ReceivedDate = DateTime.UtcNow.AddDays(-2),
                    ExpiryDate = DateTime.UtcNow.AddDays(7),
                    InspectionNote = "8株季節性花卉品質較差",
                    Status = BatchStatus.Active,
                    CreatedDate = DateTime.UtcNow.AddDays(-2)
                },
            };
        }

        private static List<Transaction> GetTransactionSeedData()
        {
            // 設定交易紀錄種子資料 - 包含入庫和出庫
            return new List<Transaction>
            {
                // 入庫交易
                new Transaction
                {
                    Id = 1,
                    FlowerId = 1,
                    BatchId = 1,
                    ChangeQty = 48,
                    TransactionType = TransactionType.In,
                    TransactionDate = DateTime.UtcNow.AddDays(-5),
                    Note = "品檢合格入庫",
                },
                new Transaction
                {
                    Id = 2,
                    FlowerId = 2,
                    BatchId = 2,
                    ChangeQty = 25,
                    TransactionType = TransactionType.In,
                    TransactionDate = DateTime.UtcNow.AddDays(-3),
                    Note = "品檢合格入庫",
                },
                new Transaction
                {
                    Id = 3,
                    FlowerId = 13,
                    BatchId = 3,
                    ChangeQty = 14,
                    TransactionType = TransactionType.In,
                    TransactionDate = DateTime.UtcNow.AddDays(-1),
                    Note = "品檢合格入庫",
                },
                new Transaction
                {
                    Id = 4,
                    FlowerId = 7,
                    BatchId = 4,
                    ChangeQty = 32,
                    TransactionType = TransactionType.In,
                    TransactionDate = DateTime.UtcNow.AddDays(-2),
                    Note = "品檢合格入庫",
                },

                // 出庫交易
                new Transaction
                {
                    Id = 5,
                    FlowerId = 1,
                    BatchId = 1,
                    ChangeQty = -10,
                    TransactionType = TransactionType.Out,
                    TransactionDate = DateTime.UtcNow.AddDays(-2),
                    Note = "銷售出貨",
                },
                new Transaction
                {
                    Id = 6,
                    FlowerId = 2,
                    BatchId = 2,
                    ChangeQty = -8,
                    TransactionType = TransactionType.Out,
                    TransactionDate = DateTime.UtcNow.AddDays(-1),
                    Note = "銷售出貨",
                },
                new Transaction
                {
                    Id = 7,
                    FlowerId = 13,
                    BatchId = 3,
                    ChangeQty = -5,
                    TransactionType = TransactionType.Out,
                    TransactionDate = DateTime.UtcNow.AddDays(-1),
                    Note = "銷售出貨",
                },
                new Transaction
                {
                    Id = 8,
                    FlowerId = 7,
                    BatchId = 4,
                    ChangeQty = -12,
                    TransactionType = TransactionType.Out,
                    TransactionDate = DateTime.UtcNow,
                    Note = "銷售出貨",
                },
            };
        }
    }
}
