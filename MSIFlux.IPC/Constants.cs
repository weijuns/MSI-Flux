using MessagePack;

namespace MSIFlux.IPC;

internal static class Constants
{
    public static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.TrustedData);
}
