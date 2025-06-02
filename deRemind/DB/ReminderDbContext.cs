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
            ConnectionString = $"Data Source={dbPath};Cache=Shared;Pooling=true;";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(ConnectionString);
            optionsBuilder.EnableSensitiveDataLogging(false);
            optionsBuilder.EnableServiceProviderCaching();
            optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.AmbientTransactionWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Reminder>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired();
                entity.Property(e => e.Description);
                entity.Property(e => e.ReminderDateTime);
                entity.Property(e => e.IsCompleted);
                entity.Property(e => e.IsRepeating);
                entity.Property(e => e.RepeatInterval)
                    .HasConversion(
                        v => v.Ticks,
                        v => new TimeSpan(v));
                entity.HasIndex(e => e.ReminderDateTime).HasDatabaseName("IX_Reminder_DateTime");
                entity.HasIndex(e => e.IsCompleted).HasDatabaseName("IX_Reminder_IsCompleted");
                entity.HasIndex(e => new { e.IsCompleted, e.ReminderDateTime }).HasDatabaseName("IX_Reminder_Completed_DateTime");
            });
        }
    }
}