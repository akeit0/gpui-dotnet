namespace Gpui.Tests;

public sealed partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData("completed-static")]
    [InlineData("pending-static")]
    [InlineData("cancelled-ready")]
    [InlineData("cancelled-pending")]
    [InlineData("faulted-ready")]
    [InlineData("faulted-pending")]
    [InlineData("producer-throws")]
    [InlineData("producer-cancels")]
    [InlineData("completed-instance")]
    [InlineData("completed-cached-instance")]
    [InlineData("completed-local-capture")]
    [InlineData("completed-cached-multicast")]
    [InlineData("completed-new-task")]
    [InlineData("pending-async-producer")]
    public void WorkAllocationPatterns(string pattern)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var scope = fixture.View.Runtime.GetWorkScope();
        var sink = new WorkAllocationSink(scope);
        const int count = 128;
        const int warmups = 4;
        const int measurements = 3;
        var isPending = pattern.Contains("pending", StringComparison.Ordinal);
        var isCancellation =
            pattern.StartsWith("cancelled", StringComparison.Ordinal)
            || pattern == "producer-cancels";
        var isFailure =
            pattern.StartsWith("faulted", StringComparison.Ordinal) || pattern == "producer-throws";
        var ready = isCancellation
            ? Task.FromCanceled<int>(new CancellationToken(true))
            : Task.FromResult(42);
        // This probe measures synchronous registration and observation on the calling thread.
        // Setup, Task/source construction, assertions, and rendering are outside the interval.
        Assert.Null(SynchronizationContext.Current);
        for (var iteration = 0; iteration < warmups + measurements; iteration++)
        {
            var sources = isPending ? new TaskCompletionSource<int>[count] : [];
            for (var index = 0; index < sources.Length; index++)
                sources[index] = new TaskCompletionSource<int>();
            // Do not reuse thrown exceptions: repeated observation can accumulate stack history.
            var failures = isFailure || pattern == "producer-cancels" ? new Exception[count] : [];
            for (var index = 0; index < failures.Length; index++)
                failures[index] = isCancellation
                    ? new OperationCanceledException()
                    : new Exception("allocation probe");
            var faultedTasks = pattern == "faulted-ready" ? new Task<int>[count] : [];
            for (var index = 0; index < faultedTasks.Length; index++)
                faultedTasks[index] = Task.FromException<int>(failures[index]);
            sink.Completed = sink.Failed = sink.Cancelled = 0;

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < count; index++)
            {
                var task =
                    isPending ? sources[index].Task
                    : faultedTasks.Length != 0 ? faultedTasks[index]
                    : ready;
                switch (pattern)
                {
                    case "completed-instance":
                        sink.StartWithInstanceCapture(task);
                        break;
                    case "completed-local-capture":
                        StartWithLocalCapture(scope, sink, task);
                        break;
                    case "completed-cached-instance":
                        scope.Start(sink, task, static (task, _) => task, sink.CachedInstance);
                        break;
                    case "completed-cached-multicast":
                        scope.Start(sink, task, static (task, _) => task, sink.CachedMulticast);
                        break;
                    case "completed-new-task":
                        scope.Start(
                            sink,
                            42,
                            static (value, _) => Task.FromResult(value),
                            CompleteAllocationWork
                        );
                        break;
                    case "producer-throws":
                    case "producer-cancels":
                        scope.Start<WorkAllocationSink, Exception, int>(
                            sink,
                            failures[index],
                            static (error, _) => throw error,
                            CompleteAllocationWork,
                            FailAllocationWork,
                            CancelAllocationWork
                        );
                        break;
                    case "pending-async-producer":
                        scope.Start(
                            sink,
                            task,
                            static async (task, _) => await task.ConfigureAwait(false),
                            CompleteAllocationWork
                        );
                        break;
                    default:
                        scope.Start(
                            sink,
                            task,
                            static (task, _) => task,
                            CompleteAllocationWork,
                            FailAllocationWork,
                            CancelAllocationWork
                        );
                        break;
                }
            }
            var afterStart = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < sources.Length; index++)
            {
                var source = sources[index];
                if (isCancellation)
                    source.SetCanceled(new CancellationToken(true));
                else if (isFailure)
                    source.SetException(failures[index]);
                else
                    source.SetResult(42);
            }
            var afterObservation = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0, sink.Completed + sink.Failed + sink.Cancelled);
            fixture.Render();
            Assert.Equal(isFailure ? count : 0, sink.Failed);
            Assert.Equal(isCancellation ? count : 0, sink.Cancelled);
            Assert.Equal(
                isFailure || isCancellation ? 0
                    : pattern == "completed-cached-multicast" ? 2 * count
                    : count,
                sink.Completed
            );
            if (iteration >= warmups)
            {
                var startBytes = (afterStart - before) / (double)count;
                var observationBytes = (afterObservation - afterStart) / (double)count;
                TestContext.Current.TestOutputHelper!.WriteLine(
                    $"{pattern}: start={startBytes:N1}, observation={observationBytes:N1}, total={startBytes + observationBytes:N1} B/op"
                );
                // Detect accidental wrapper Tasks/closures on the basic paths, while leaving
                // fault paths free to account for runtime exception/stack-trace allocation.
                if (pattern is "completed-static" or "cancelled-ready")
                    Assert.InRange(startBytes + observationBytes, 1, 128);
                if (pattern is "pending-static" or "cancelled-pending")
                    Assert.InRange(startBytes + observationBytes, 1, 256);
            }
        }
    }

    private static void CompleteAllocationWork(WorkAllocationSink state, int _) =>
        state.Completed++;

    private static void FailAllocationWork(WorkAllocationSink state, Exception _) => state.Failed++;

    private static void CancelAllocationWork(WorkAllocationSink state) => state.Cancelled++;

    private static void StartWithLocalCapture(
        WorkScope scope,
        WorkAllocationSink sink,
        Task<int> task
    ) => scope.Start(sink, task, static (task, _) => task, (_, _) => sink.Completed++);

    private sealed class WorkAllocationSink
    {
        private readonly WorkScope _scope;
        internal readonly Action<WorkAllocationSink, int> CachedInstance;
        internal readonly Action<WorkAllocationSink, int> CachedMulticast;
        internal int Completed;
        internal int Failed;
        internal int Cancelled;

        internal WorkAllocationSink(WorkScope scope)
        {
            _scope = scope;
            CachedInstance = Complete;
            CachedMulticast =
                (Action<WorkAllocationSink, int>)CompleteAllocationWork + CompleteAllocationWork;
        }

        private void Complete(WorkAllocationSink _, int value) => Completed++;

        internal void StartWithInstanceCapture(Task<int> task) =>
            _scope.Start(this, task, static (task, _) => task, (_, _) => Completed++);
    }
}
