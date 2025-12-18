using Photonize.Services;
using Xunit;

namespace Photonize.Tests;

public class WorkerQueueTests
{
    [Fact]
    public void Constructor_WithValidWorkerCount_CreatesQueue()
    {
        using var queue = new WorkerQueue<int>(workerCount: 2);
        Assert.NotNull(queue);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Constructor_WithZeroWorkers_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WorkerQueue<int>(workerCount: 0));
    }

    [Fact]
    public void Constructor_WithNegativeWorkers_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WorkerQueue<int>(workerCount: -1));
    }

    [Fact]
    public void Enqueue_WithNullItem_ThrowsArgumentNullException()
    {
        using var queue = new WorkerQueue<string>();
        Assert.Throws<ArgumentNullException>(() => 
            queue.Enqueue(null!, (item, ct) => Task.CompletedTask));
    }

    [Fact]
    public void Enqueue_WithNullWorkFunc_ThrowsArgumentNullException()
    {
        using var queue = new WorkerQueue<int>();
        Assert.Throws<ArgumentNullException>(() => 
            queue.Enqueue(42, null!));
    }

    [Fact]
    public void Enqueue_SingleItem_ProcessesSuccessfully()
    {
        using var queue = new WorkerQueue<int>(workerCount: 1);
        var processed = false;

        queue.Enqueue(42, async (item, ct) =>
        {
            await Task.Delay(10, ct);
            processed = true;
        });

        var completed = queue.WaitForCompletion(TimeSpan.FromSeconds(5));
        
        Assert.True(completed);
        Assert.True(processed);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Enqueue_MultipleItems_ProcessesInFIFOOrder()
    {
        using var queue = new WorkerQueue<int>(workerCount: 1); // Single worker for deterministic order
        var processedItems = new List<int>();
        var lockObj = new object();

        for (int i = 1; i <= 5; i++)
        {
            var item = i;
            queue.Enqueue(item, async (num, ct) =>
            {
                await Task.Delay(10, ct);
                lock (lockObj)
                {
                    processedItems.Add(num);
                }
            });
        }

        var completed = queue.WaitForCompletion(TimeSpan.FromSeconds(5));
        
        Assert.True(completed);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, processedItems);
    }

    [Fact]
    public void Enqueue_WithMultipleWorkers_ProcessesAllItems()
    {
        using var queue = new WorkerQueue<int>(workerCount: 4);
        var processedCount = 0;
        var lockObj = new object();
        var itemCount = 20;

        for (int i = 0; i < itemCount; i++)
        {
            queue.Enqueue(i, async (item, ct) =>
            {
                await Task.Delay(50, ct);
                lock (lockObj)
                {
                    processedCount++;
                }
            });
        }

        var completed = queue.WaitForCompletion(TimeSpan.FromSeconds(10));
        
        Assert.True(completed);
        Assert.Equal(itemCount, processedCount);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void WorkItemCompleted_Event_RaisedOnSuccess()
    {
        using var queue = new WorkerQueue<string>(workerCount: 1);
        var completedItems = new List<string>();
        var lockObj = new object();

        queue.WorkItemCompleted += (sender, args) =>
        {
            lock (lockObj)
            {
                completedItems.Add(args.Item);
            }
        };

        queue.Enqueue("test1", (item, ct) => Task.CompletedTask);
        queue.Enqueue("test2", (item, ct) => Task.CompletedTask);

        queue.WaitForCompletion(TimeSpan.FromSeconds(5));

        Assert.Equal(2, completedItems.Count);
        Assert.Contains("test1", completedItems);
        Assert.Contains("test2", completedItems);
    }

    [Fact]
    public void WorkItemFailed_Event_RaisedOnException()
    {
        using var queue = new WorkerQueue<int>(workerCount: 1);
        var failedItems = new List<int>();
        var exceptions = new List<Exception>();
        var lockObj = new object();

        queue.WorkItemFailed += (sender, args) =>
        {
            lock (lockObj)
            {
                failedItems.Add(args.Item);
                exceptions.Add(args.Exception);
            }
        };

        queue.Enqueue(42, (item, ct) => throw new InvalidOperationException("Test error"));

        queue.WaitForCompletion(TimeSpan.FromSeconds(5));

        Assert.Single(failedItems);
        Assert.Equal(42, failedItems[0]);
        Assert.Single(exceptions);
        Assert.IsType<InvalidOperationException>(exceptions[0]);
    }

    [Fact]
    public void Enqueue_AfterDispose_ThrowsObjectDisposedException()
    {
        var queue = new WorkerQueue<int>();
        queue.Dispose();

        Assert.Throws<ObjectDisposedException>(() => 
            queue.Enqueue(42, (item, ct) => Task.CompletedTask));
    }

    [Fact]
    public void WaitForCompletion_WithTimeout_ReturnsFalseWhenNotComplete()
    {
        using var queue = new WorkerQueue<int>(workerCount: 1);

        queue.Enqueue(1, async (item, ct) => await Task.Delay(5000, ct));

        var completed = queue.WaitForCompletion(TimeSpan.FromMilliseconds(100));

        Assert.False(completed);
    }

    [Fact]
    public void PendingCount_ReflectsQueueState()
    {
        using var queue = new WorkerQueue<int>(workerCount: 1);
        
        Assert.Equal(0, queue.PendingCount);

        // Add items faster than they can be processed
        for (int i = 0; i < 10; i++)
        {
            queue.Enqueue(i, async (item, ct) => await Task.Delay(100, ct));
        }

        // Should have items pending
        Assert.True(queue.PendingCount > 0);

        queue.WaitForCompletion(TimeSpan.FromSeconds(5));
        
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Shutdown_StopsAcceptingNewWork()
    {
        var queue = new WorkerQueue<int>(workerCount: 2);

        queue.Enqueue(1, async (item, ct) => await Task.Delay(50, ct));
        
        queue.Shutdown();

        // Queue should complete gracefully
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void CancellationToken_CancelsWorkItem()
    {
        using var queue = new WorkerQueue<int>(workerCount: 1);
        using var cts = new CancellationTokenSource();
        var cancelled = false;

        queue.Enqueue(1, async (item, ct) =>
        {
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
        }, cts.Token);

        Thread.Sleep(100); // Let work start
        cts.Cancel();

        queue.WaitForCompletion(TimeSpan.FromSeconds(2));

        Assert.True(cancelled);
    }

    [Fact]
    public void ParallelProcessing_WithFourWorkers_ProcessesConcurrently()
    {
        using var queue = new WorkerQueue<int>(workerCount: 4);
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        for (int i = 0; i < 8; i++)
        {
            queue.Enqueue(i, async (item, ct) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    if (concurrentCount > maxConcurrent)
                        maxConcurrent = concurrentCount;
                }

                await Task.Delay(200, ct);

                lock (lockObj)
                {
                    concurrentCount--;
                }
            });
        }

        queue.WaitForCompletion(TimeSpan.FromSeconds(10));

        // With 4 workers and 8 items, we should see at least 2-4 concurrent
        Assert.InRange(maxConcurrent, 2, 4);
    }
}
