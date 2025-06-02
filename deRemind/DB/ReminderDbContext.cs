using deRemind.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace deRemind.Data
{
    public class ReminderDbContext : DbContext
    {
        private static readonly string ConnectionString;
        public DbSet<Reminder> Reminders { get; set; }

        static ReminderDbContext()
        {
            var localFolder = ApplicationData.Current.LocalFolder.Path;
            var dbPath = Path.Combine(localFolder, "reminders.db");
            ConnectionString = $"Data Source={dbPath};Cache=Shared;Pooling=true;Journal Mode=WAL;Synchronous=NORMAL;";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(ConnectionString);
            optionsBuilder.EnableSensitiveDataLogging(false);
            optionsBuilder.EnableServiceProviderCaching();
            optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.AmbientTransactionWarning));

            // Performance optimizations
            optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Reminder>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.ReminderDateTime);
                entity.Property(e => e.IsCompleted);
                entity.Property(e => e.IsRepeating);
                entity.Property(e => e.RepeatInterval)
                    .HasConversion(
                        v => v.Ticks,
                        v => new TimeSpan(v));

                // Optimized composite index for most common queries
                entity.HasIndex(e => new { e.IsCompleted, e.ReminderDateTime, e.IsRepeating })
                    .HasDatabaseName("IX_Reminder_Composite_Main");
                entity.HasIndex(e => e.ReminderDateTime).HasDatabaseName("IX_Reminder_DateTime");
            });
        }

        // Connection pool management
        public static async Task WarmupConnectionPoolAsync()
        {
            using var context = new ReminderDbContext();
            await context.Database.EnsureCreatedAsync();
            await context.Reminders.CountAsync(); // Warm up the connection
        }
    }

    // Database service with connection pooling and caching
    public class DatabaseService
    {
        private static readonly Lazy<DatabaseService> _instance = new(() => new DatabaseService());
        public static DatabaseService Instance => _instance.Value;

        private readonly object _contextLock = new();
        private ReminderDbContext? _context;
        private DateTime _lastAccess = DateTime.Now;
        private readonly TimeSpan _contextTimeout = TimeSpan.FromMinutes(5);

        public ReminderDbContext GetContext()
        {
            lock (_contextLock)
            {
                // Dispose old context if it's been idle too long
                if (_context != null && DateTime.Now - _lastAccess > _contextTimeout)
                {
                    _context.Dispose();
                    _context = null;
                }

                _context ??= new ReminderDbContext();
                _lastAccess = DateTime.Now;
                return _context;
            }
        }

        public void DisposeContext()
        {
            lock (_contextLock)
            {
                _context?.Dispose();
                _context = null;
            }
        }
    }
}