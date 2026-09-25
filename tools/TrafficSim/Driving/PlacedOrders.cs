using System.Collections.Concurrent;

namespace ElGuerre.Tendero.TrafficSim.Driving;

/// <summary>
/// The orders this run has placed, by the address of their page — which for a
/// guest is the credential (a GUID v7 nobody can enumerate).
///
/// It is the one place where a session depends on what another did: a customer
/// coming back to an order can only come back to one that exists. Which of them
/// is still drawn from the session's own stream; only the list it draws from is
/// shared, and a returner who finds none skips the step and says so.
/// </summary>
public sealed class PlacedOrders
{
    private readonly ConcurrentQueue<string> paths = new();

    public int Count => this.paths.Count;

    public void Add(string path) => this.paths.Enqueue(path);

    public string? Pick(Random random)
    {
        var snapshot = this.paths.ToArray();
        return snapshot.Length == 0 ? null : snapshot[random.Next(snapshot.Length)];
    }
}
