using Microsoft.Extensions.Configuration;

namespace LoadBalancer;

public sealed class SignalConfigurationSource : IConfigurationSource
{
    public SignalConfigurationProvider Provider { get; } = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

public sealed class SignalConfigurationProvider : ConfigurationProvider
{
    public void TriggerReload() => OnReload();
}
