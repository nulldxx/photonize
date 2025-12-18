using System.Collections.Concurrent;

namespace Photonize.Services;

/// <summary>
/// Generic worker queue that processes work items in parallel using a fixed number of worker threads.
/// Work items are processed in FIFO order by available workers.
/// </summary>
/// <typeparam name="TWorkItem">Type of work item to process</typeparam>
public class WorkerQueue<TWorkItem> : IDisposable
{
    private readonly int _workerCount;
    private readonly BlockingCollection<WorkItem> _workQueue;
    private readonly List<Thread> _workers;
    private readonly CancellationTokenSource _shutdownTokenSource;
    private bool _disposed;

    /// <summary>
    /// Gets the current number of pending work items in the queue
    /// </summary>
    public int PendingCount => _workQueue.Count;

    /// <summary>
    /// Event raised when a work item completes successfully
    /// </summary>
    public event EventHandler<WorkItemCompletedEventArgs>? WorkItemCompleted;

    /// <summary>
    /// Event raised when a work item fails with an exception
    /// </summary>
    public event EventHandler<WorkItemFailedEventArgs>? WorkItemFailed;

    private class WorkItem
    {
        public TWorkItem Item { get; set; }
        public Func<TWorkItem, CancellationToken, Task> WorkFunc { get; set; }
        public CancellationToken CancellationToken { get; set; }

        public WorkItem(TWorkItem item, Func<TWorkItem, CancellationToken, Task> workFunc, CancellationToken cancellationToken)
        {
            Item = item;
            WorkFunc = workFunc;
            CancellationToken = cancellationToken;
        }
    }

    public class WorkItemCompletedEventArgs : EventArgs
    {
        public TWorkItem Item { get; }
        public WorkItemCompletedEventArgs(TWorkItem item)
        {
            Item = item;
        }
    }

    public class WorkItemFailedEventArgs : EventArgs
    {
        public TWorkItem Item { get; }
        public Exception Exception { get; }
        public WorkItemFailedEventArgs(TWorkItem item, Exception exception)
        {
            Item = item;
            Exception = exception;
        }
    }

    /// <summary>
    /// Creates a new worker queue with the specified number of worker threads
    /// </summary>
    /// <param name="workerCount">Number of worker threads (default: 4)</param>
    public WorkerQueue(int workerCount = 4)
    {
        if (workerCount <= 0)
            throw new ArgumentException("Worker count must be greater than zero", nameof(workerCount));

        _workerCount = workerCount;
        _workQueue = new BlockingCollection<WorkItem>();
        _workers = new List<Thread>();
        _shutdownTokenSource = new CancellationTokenSource();

        StartWorkers();
    }

    private void StartWorkers()
    {
        for (int i = 0; i < _workerCount; i++)
        {
            var worker = new Thread(WorkerLoop)
            {
                Name = $"WorkerQueue-{typeof(TWorkItem).Name}-{i}",
                IsBackground = true
            };
            worker.Start();
            _workers.Add(worker);
        }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var workItem in _workQueue.GetConsumingEnumerable(_shutdownTokenSource.Token))
            {
                if (_shutdownTokenSource.Token.IsCancellationRequested)
                    break;

                try
                {
                    // Execute the async work and wait for completion
                    workItem.WorkFunc(workItem.Item, workItem.CancellationToken).GetAwaiter().GetResult();
                    
                    WorkItemCompleted?.Invoke(this, new WorkItemCompletedEventArgs(workItem.Item));
                }
                catch (Exception ex)
                {
                    WorkItemFailed?.Invoke(this, new WorkItemFailedEventArgs(workItem.Item, ex));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// Enqueues a work item to be processed by an available worker
    /// </summary>
    /// <param name="item">The work item to process</param>
    /// <param name="workFunc">Async function that processes the work item</param>
    /// <param name="cancellationToken">Optional cancellation token for this specific work item</param>
    public void Enqueue(TWorkItem item, Func<TWorkItem, CancellationToken, Task> workFunc, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerQueue<TWorkItem>));

        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (workFunc == null)
            throw new ArgumentNullException(nameof(workFunc));

        _workQueue.Add(new WorkItem(item, workFunc, cancellationToken));
    }

    /// <summary>
    /// Waits for all pending work items to complete
    /// </summary>
    /// <param name="timeout">Optional timeout. If null, waits indefinitely.</param>
    /// <returns>True if all work completed within the timeout, false otherwise</returns>
    public bool WaitForCompletion(TimeSpan? timeout = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerQueue<TWorkItem>));

        var startTime = DateTime.UtcNow;
        while (_workQueue.Count > 0)
        {
            if (timeout.HasValue && DateTime.UtcNow - startTime > timeout.Value)
                return false;

            Thread.Sleep(10);
        }

        return true;
    }

    /// <summary>
    /// Stops accepting new work and waits for all workers to finish current work
    /// </summary>
    public void Shutdown()
    {
        if (_disposed)
            return;

        _workQueue.CompleteAdding();

        foreach (var worker in _workers)
        {
            worker.Join();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _workQueue.CompleteAdding();
        _shutdownTokenSource.Cancel();

        foreach (var worker in _workers)
        {
            if (!worker.Join(TimeSpan.FromSeconds(5)))
            {
                // Worker didn't stop gracefully, but we can't abort threads in modern .NET
            }
        }

        _workQueue.Dispose();
        _shutdownTokenSource.Dispose();
    }
}
