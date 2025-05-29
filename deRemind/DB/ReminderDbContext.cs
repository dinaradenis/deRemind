using Microsoft.EntityFrameworkCore;
using deRemind.Models;
using System.IO;
using Windows.Storage;
using System;

namespace deRemind.Data
{
    public class ReminderDbContext : DbContext
    {
        public DbSet<Reminder> Reminders { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var localFolder = ApplicationData.Current.LocalFolder.Path;
            var dbPath = Path.Combine(localFolder, "reminders.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
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
            });
        }
    }
}