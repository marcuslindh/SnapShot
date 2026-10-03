using System.Threading;

namespace SnapShot;

/// <summary>Entry point.</summary>
internal static class Program
{
    private const string SingleInstanceName = @"Local\SnapShot.SingleInstance";

    private static int Main()
    {
        // Two instances would install two hooks and both react to the same key press.
        using Mutex singleInstance = new(initiallyOwned: true, SingleInstanceName, out bool createdNew);

        if (!createdNew)
        {
            return 0;
        }

        using SnapShotApp app = SnapShotApp.Start();

        return SnapShotApp.Run();
    }
}
