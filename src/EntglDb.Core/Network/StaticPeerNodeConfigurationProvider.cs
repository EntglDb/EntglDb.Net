using System.Threading.Tasks;

namespace EntglDb.Core.Network;

public class StaticPeerNodeConfigurationProvider : IPeerNodeConfigurationProvider
{
    private PeerNodeConfiguration _configuration;
    public PeerNodeConfiguration Configuration
    {
        get => _configuration;
        set
        {
            if (_configuration != value)
            {
                _configuration = value;
                OnConfigurationChanged(_configuration);
            }
        }
    }

    public StaticPeerNodeConfigurationProvider(PeerNodeConfiguration configuration)
    {
        Configuration = configuration;
    }

    public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;

    public Task<PeerNodeConfiguration> GetConfiguration()
    {
        return Task.FromResult(Configuration);
    }

    protected virtual void OnConfigurationChanged(PeerNodeConfiguration newConfig)
    {
        ConfigurationChanged?.Invoke(this, newConfig);
    }
}
