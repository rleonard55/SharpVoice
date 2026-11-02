namespace SharpVoice.Refiner;

/// <summary>Restores saved history before allowing readiness or subsequent disk mutations.</summary>
public sealed class HistoryWorkQueue
{
    public Task Ready { get; }
    public Task Completion { get; private set; }
    public HistoryWorkQueue(Action? load = null)
    {
        Ready = load is null ? Task.CompletedTask : Task.Run(load);
        Completion = Ready;
    }
    public void Enqueue(Action write)
    {
        var previous = Completion;
        Completion = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            write();
        });
    }
}
