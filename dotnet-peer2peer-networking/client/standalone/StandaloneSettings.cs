namespace NetworkingDemo.Client.Settings;

#if DEBUG
public class StandaloneSettings : IClientSettings
{
    public string DisplayName { get; }
}
#endif
