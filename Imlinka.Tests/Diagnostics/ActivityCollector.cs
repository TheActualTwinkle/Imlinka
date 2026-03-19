using System.Collections.Concurrent;
using System.Diagnostics;

namespace Imlinka.Tests.Diagnostics;

internal sealed class ActivityCollector : IDisposable
{
    private readonly ConcurrentQueue<Activity> _started = new();
    private readonly ActivityListener _listener;

    public ActivityCollector()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = SampleActivity,
            SampleUsingParentId = SampleUsingParentIdActivity,
            ActivityStarted = activity => _started.Enqueue(activity)
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Started => _started.ToArray();

    public void Dispose() => _listener.Dispose();

    private static ActivitySamplingResult SampleActivity(ref ActivityCreationOptions<ActivityContext> _) =>
        ActivitySamplingResult.AllDataAndRecorded;

    private static ActivitySamplingResult SampleUsingParentIdActivity(ref ActivityCreationOptions<string> _) =>
        ActivitySamplingResult.AllDataAndRecorded;
}