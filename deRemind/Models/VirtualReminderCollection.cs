using deRemind.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace deRemind.Models
{
    public class VirtualReminderCollection : ObservableCollection<Reminder>
    {
        private readonly ReminderDbContext _context;
        private readonly int _pageSize = 50;
        private bool _isLoading = false;

        public VirtualReminderCollection(ReminderDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task LoadPageAsync(int page = 0)
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                var reminders = await _context.Reminders
                    .Where(r => !r.IsCompleted || r.ReminderDateTime > DateTime.Now.AddDays(-7))
                    .OrderBy(r => r.ReminderDateTime)
                    .Skip(page * _pageSize)
                    .Take(_pageSize)
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var reminder in reminders)
                {
                    Add(reminder);
                }
            }
            finally
            {
                _isLoading = false;
            }
        }
    }
}
