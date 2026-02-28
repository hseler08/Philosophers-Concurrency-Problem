using System.Diagnostics;

sealed class Stats
{
    public long[] WaitTicks;
    public int[] Meals;

    public Stats(int n)
    {
        WaitTicks = new long[n];
        Meals = new int[n];
    }
}

interface IForksStrategy : IDisposable
{
    bool Acquire(int id, CancellationToken token);
    void Release(int id);
    string Name { get; }
}

sealed class AtomicBothStrategy : IForksStrategy
{
    private readonly int _nr;
    private readonly object _lockObj = new object();
    private readonly bool[] _inUse;

    public string Name => "AtomicBoth";

    public AtomicBothStrategy(int nr)
    {
        this._nr = nr;
        _inUse = new bool[nr];
    }

    public bool Acquire(int id, CancellationToken token)
    {
        int left = id;
        int right = (id + 1) % _nr;

        while (!token.IsCancellationRequested)
        {
            lock (_lockObj)
            {
                if (!_inUse[left] && !_inUse[right])
                {
                    _inUse[left] = true;
                    _inUse[right] = true;
                    return true;
                }
            }
            Thread.Sleep(1);
        }
        return false;
    }

    public void Release(int id)
    {
        int left = id;
        int right = (id + 1) % _nr;

        lock (_lockObj)
        {
            _inUse[left] = false;
            _inUse[right] = false;
        }
    }

    public void Dispose() { }
}

sealed class OrderedLockingStrategy : IForksStrategy
{
    private readonly int _nr;
    private readonly object[] _forkLocks;

    public string Name => "OrderedLocking";

    public OrderedLockingStrategy(int nr)
    {
        this._nr = nr;
        _forkLocks = new object[nr];
        for (int i = 0; i < nr; i++) _forkLocks[i] = new object();
    }

    public bool Acquire(int id, CancellationToken token)
    {
        int left = id;
        int right = (id + 1) % _nr;

        int first = Math.Min(left, right);
        int second = Math.Max(left, right);

        if (token.IsCancellationRequested) return false;
        
        Monitor.Enter(_forkLocks[first]);
        if (token.IsCancellationRequested)
        {
            Monitor.Exit(_forkLocks[first]);
            return false;
        }

        Monitor.Enter(_forkLocks[second]);
        return true;
    }

    public void Release(int id)
    {
        int left = id;
        int right = (id + 1) % _nr;

        int first = Math.Min(left, right);
        int second = Math.Max(left, right);

        Monitor.Exit(_forkLocks[second]);
        Monitor.Exit(_forkLocks[first]);
    }

    public void Dispose() { }
}

static class Program
{
    // true  = pokazuje symulację na konsoli, false = bez symulacji
    static bool _showSimulation = true;

    static readonly object ConsoleLock = new object();

    static readonly int[] N = { 6 };
    const int Repeats = 1;
    static readonly TimeSpan Warmup = TimeSpan.FromSeconds(2);
    static readonly TimeSpan Measure = TimeSpan.FromSeconds(10);

    static readonly ThreadLocal<Random> Rng =
        new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

    static StreamWriter? _out;
    static readonly object FileLock = new object();

    static void Main()
    {
        _out = new StreamWriter("wyniki.txt", append: false) { AutoFlush = true };
        WriteFile("strategy\tN\tphilosopher\tavg_wait_ms");

        Console.WriteLine($"Warmup={Warmup.TotalSeconds}s, Measure={Measure.TotalSeconds}s, Repeats={Repeats}");
        Console.WriteLine($"_showSimulation={_showSimulation}");
        Console.WriteLine("Zapis do pliku: wyniki.txt");
        Console.WriteLine();

        foreach (int n in N)
        {
            RunCase(n, () => new AtomicBothStrategy(n));
            RunCase(n, () => new OrderedLockingStrategy(n));
            Console.WriteLine(new string('-', 60));
        }

        _out.Dispose();
        Console.WriteLine("Koniec. Wyniki zapisane do wyniki.txt");
    }

    static void RunCase(int n, Func<IForksStrategy> makeStrategy)
    {
        using IForksStrategy strategy = makeStrategy();
        Console.WriteLine($"Start: N={n}, Strategy={strategy.Name}");

        double[] sumAvgWaitMs = new double[n];

        for (int r = 0; r < Repeats; r++)
        {
            Console.WriteLine($"  Repeat {r + 1}/{Repeats}");
            var stats = RunSingle(n, strategy);

            for (int i = 0; i < n; i++)
            {
                double avgMs = stats.Meals[i] > 0
                    ? TicksToMs(stats.WaitTicks[i]) / stats.Meals[i]
                    : double.PositiveInfinity;

                sumAvgWaitMs[i] += avgMs;
            }
        }

        double sumFinite = 0.0;
        int finiteCount = 0;

        for (int i = 0; i < n; i++)
        {
            double mean = sumAvgWaitMs[i] / Repeats;
            WriteFile($"{strategy.Name}\t{n}\t{i}\t{Fmt(mean)}");

            if (!double.IsInfinity(mean) && !double.IsNaN(mean))
            {
                sumFinite += mean;
                finiteCount++;
            }
        }

        double allMean = finiteCount > 0 ? (sumFinite / finiteCount) : double.PositiveInfinity;
        WriteFile($"{strategy.Name}\t{n}\tALL\t{Fmt(allMean)}");

        Console.WriteLine($"Done: N={n}, Strategy={strategy.Name}");
        Console.WriteLine();
    }

    static Stats RunSingle(int n, IForksStrategy strategy)
    {
        var stats = new Stats(n);
        using var cts = new CancellationTokenSource();

        long freq = Stopwatch.Frequency;
        long start = Stopwatch.GetTimestamp();
        long measureStart = start + (long)(Warmup.TotalSeconds * freq);
        long measureEnd = measureStart + (long)(Measure.TotalSeconds * freq);

        if (_showSimulation) Log($"--- START symulacji: {strategy.Name}, N={n} ---");

        _ = Task.Run(() =>
        {
            while (Stopwatch.GetTimestamp() < measureEnd) Thread.Sleep(10);
            cts.Cancel();
        });

        Task[] tasks = new Task[n];
        for (int id = 0; id < n; id++)
        {
            int pid = id;
            tasks[id] = Task.Factory.StartNew(
                () =>
                {
                    int left = pid;
                    int right = (pid + 1) % n;

                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (_showSimulation) Log($"[{Ts()}] {strategy.Name} | F{pid} myśli");
                        Thread.Sleep(Rng.Value!.Next(20, 80));

                        if (_showSimulation) Log($"[{Ts()}] {strategy.Name} | F{pid} PRÓBUJE ({left},{right})");

                        long waitStart = Stopwatch.GetTimestamp();
                        if (!strategy.Acquire(pid, cts.Token)) break;
                        long acquiredAt = Stopwatch.GetTimestamp();

                        if (_showSimulation) Log($"[{Ts()}] {strategy.Name} | F{pid} PODNIOSŁ ({left},{right})");

                        if (acquiredAt >= measureStart)
                        {
                            Interlocked.Add(ref stats.WaitTicks[pid], acquiredAt - waitStart);
                            Interlocked.Increment(ref stats.Meals[pid]);
                        }

                        if (_showSimulation) Log($"[{Ts()}] {strategy.Name} | F{pid} JE");
                        Thread.Sleep(Rng.Value!.Next(15, 60));

                        strategy.Release(pid);
                        if (_showSimulation) Log($"[{Ts()}] {strategy.Name} | F{pid} ODŁOŻYŁ ({left},{right})");
                    }
                },
                cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        try { Task.WaitAll(tasks); } catch (AggregateException) { }

        if (_showSimulation) Log($"--- STOP symulacji: {strategy.Name}, N={n} ---");
        return stats;
    }

    static void WriteFile(string line)
    {
        lock (FileLock)
        {
            _out!.WriteLine(line);
        }
    }

    static void Log(string msg)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine(msg);
        }
    }

    static string Ts() => DateTime.Now.ToString("HH:mm:ss.fff");

    static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    static string Fmt(double v) => double.IsInfinity(v) ? "INF" : v.ToString("0.###");
}