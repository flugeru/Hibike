using Hibike.Core.Services;
using Xunit;

namespace Hibike.Core.Tests;

public sealed class LatestSnapshotWriterTests
{
    [Fact]
    public async Task WritesOnWorkerAndKeepsLatestSnapshotDuringSlowWrite()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var caller = Environment.CurrentManagedThreadId;
        var threads = new List<int>(); var saved = new List<string>();
        var writer = new LatestSnapshotWriter<string>(value =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            if (value == "first") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new IOException("Timeout"); }
            saved.Add(value);
        }, _ => { });
        writer.Enqueue("first");
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            writer.Enqueue("second"); writer.Enqueue("latest");
        }
        finally { release.Set(); }
        await writer.FlushAsync();
        Assert.Equal(new[] { "first", "latest" }, saved);
        Assert.All(threads, thread => Assert.NotEqual(caller, thread));
    }

    [Fact]
    public async Task ReportsFailureAndCanSaveAgain()
    {
        var errors = new List<Exception>(); var written = "";
        var writer = new LatestSnapshotWriter<string>(value =>
        { if (value == "fail") throw new IOException("Disk unavailable"); written = value; }, errors.Add);
        writer.Enqueue("fail"); await Assert.ThrowsAsync<IOException>(writer.FlushAsync);
        writer.Enqueue("recovered"); await writer.FlushAsync();
        Assert.Single(errors); Assert.Equal("recovered", written);
    }

    [Fact]
    public void SynchronousFlushPersistsLatestSnapshotBeforeReturning()
    {
        string? written = null;
        var writer = new LatestSnapshotWriter<string>(value => written = value, _ => { });
        writer.Enqueue("old"); writer.Enqueue("final");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(5)));
        Assert.Equal("final", written);
    }

    [Fact]
    public void SynchronousFlushReportsWriteFailure()
    {
        var writer = new LatestSnapshotWriter<string>(_ => throw new IOException("Disk unavailable"), _ => { });
        writer.Enqueue("value");
        Assert.Throws<IOException>(() => writer.Flush(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task FlushSkipsCoalescingDelay()
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new LatestSnapshotWriter<string>(_ => written.TrySetResult(), _ => { });
        // Repeat to reduce scheduler noise; each flush must not wait for the 50 ms grouping window.
        var elapsed = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            writer.Enqueue("value" + i);
            await writer.FlushAsync();
            elapsed.Add(watch.Elapsed.TotalMilliseconds);
            Assert.True(written.Task.IsCompleted);
        }
        elapsed.Sort();
        Assert.True(elapsed[2] < 40, $"Median flush took {elapsed[2]:0.0} ms");
    }
}
