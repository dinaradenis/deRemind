using System;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks;

public class BackgroundOperationQueue
{
    private readonly Channel<Func<Task>> _queue;
    private readonly ChannelWriter<Func<Task>> _writer;
    private readonly Task _processingTask;

    public BackgroundOperationQueue()
    {
        var options = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        };

        _queue = Channel.CreateBounded<Func<Task>>(options);
        _writer = _queue.Writer;
        _processingTask = ProcessQueueAsync();
    }

    public async Task<bool> EnqueueAsync(Func<Task> operation)
    {
        return await _writer.WaitToWriteAsync() && _writer.TryWrite(operation);
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var operation in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Background operation failed: {ex.Message}");
            }
        }
    }
}
