namespace LoadBalancer;

public sealed class LbState
{
    private readonly SignalConfigurationProvider _signal;
    private readonly object _lock = new();

    public static readonly string[] AllowedAlgorithms =
    [
        "RoundRobin",
        "LeastRequests",
        "Random",
        "PowerOfTwoChoices"
    ];

    public string Algorithm { get; private set; } = "RoundRobin";
    public string Mode { get; private set; } = "Stateless";

    public LbState(IConfiguration config, SignalConfigurationProvider signal)
    {
        _signal = signal;
        var initialMode = config["LoadBalancer:Mode"];
        if (!string.IsNullOrWhiteSpace(initialMode))
        {
            Mode = initialMode;
        }

        var initialAlgo = config["LoadBalancer:Algorithm"];
        if (!string.IsNullOrWhiteSpace(initialAlgo) && AllowedAlgorithms.Contains(initialAlgo, StringComparer.OrdinalIgnoreCase))
        {
            Algorithm = initialAlgo;
        }
    }

    public bool SetAlgorithm(string newAlgo)
    {
        var matched = AllowedAlgorithms.FirstOrDefault(a => string.Equals(a, newAlgo, StringComparison.OrdinalIgnoreCase));
        if (matched is null) return false;

        lock (_lock)
        {
            if (Algorithm == matched) return true;
            Algorithm = matched;
        }

        _signal.TriggerReload();
        return true;
    }

    public bool SetMode(string newMode)
    {
        string normalized = string.Equals(newMode, "Affinity", StringComparison.OrdinalIgnoreCase) ? "Affinity" : "Stateless";
        lock (_lock)
        {
            if (Mode == normalized) return true;
            Mode = normalized;
        }

        _signal.TriggerReload();
        return true;
    }
}
