using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using OpenTelemetry.Trace;

namespace Company.Service.ApiTests;

/// <summary>
/// Hosts the real application in memory. Only the edges are swapped: configuration for the test environment
/// and an in-memory trace exporter so tests can assert on spans.
/// </summary>
public class ServiceFactory : WebApplicationFactory<Program>
{
    public ExportedCollection<Activity> Spans { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Observability:LogFormat", "Text");

        builder.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(Spans)));
    }
}

/// <summary>Thread-safe collection for exporters that write from request threads while a test reads.</summary>
public sealed class ExportedCollection<T> : ICollection<T>
{
    private readonly List<T> _items = [];
    private readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public T[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }

    /// <summary>Spans end slightly after the response is sent, so poll briefly instead of asserting at once.</summary>
    public async Task<T> WaitForAsync(Func<T, bool> predicate, CancellationToken cancellationToken)
    {
        var deadline = TimeProvider.System.GetTimestamp() + TimeProvider.System.TimestampFrequency * 5;
        while (TimeProvider.System.GetTimestamp() < deadline)
        {
            var match = Snapshot().FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("No exported item matched within 5 seconds.");
    }

    public void Add(T item)
    {
        lock (_gate)
        {
            _items.Add(item);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }

    public bool Contains(T item) => Snapshot().Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => Snapshot().CopyTo(array, arrayIndex);

    public bool Remove(T item)
    {
        lock (_gate)
        {
            return _items.Remove(item);
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Snapshot()).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
