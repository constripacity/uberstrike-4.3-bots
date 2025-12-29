using BotRunner.Config;

namespace BotRunner.Networking
{
    /// <summary>
    /// Creates the appropriate transport: mock by default, Photon-backed when PHOTON3 is defined.
    /// This keeps the sample runnable without Photon binaries while allowing a seamless upgrade.
    /// </summary>
    public static class TransportConnectionFactory
    {
        public static ITransportConnection Create(AppSettings settings)
        {
#if PHOTON3
            return new Photon3TransportConnection(settings.Server.Endpoint, settings.PhotonAppId);
#else
            return new MockTransportConnection();
#endif
        }
    }
}
