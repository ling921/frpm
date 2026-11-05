namespace Frpm.Desktop.Tests;

public sealed class DesktopInstanceTests
{
    [Fact]
    public async Task Second_launch_activates_the_existing_instance_without_opening_database_twice()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"frpm-instance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.True(DesktopInstance.TryAcquire(directory, out var instance));
            using (instance!)
            {
                var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                instance!.Listen(() => activated.SetResult(), () => { });
                Assert.False(DesktopInstance.TryAcquire(directory, out _));
                await DesktopInstance.ActivateExistingAsync(directory);
                await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.True(DesktopInstance.TryAcquire(directory, out var next));
            next!.Dispose();
        }
        finally
        {
            if (Path.GetDirectoryName(directory) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)) Directory.Delete(directory, true);
        }
    }
}
