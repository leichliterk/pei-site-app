namespace PeiSiteService;

/// <summary>
/// Environment-specific path constants. Staging and production use separate
/// data directories and registry keys so they can coexist on the same machine.
/// </summary>
internal static class ServicePaths
{
#if PRODUCTION
    internal const string DataDirName    = "PEI Site Service";
    internal const string RegistryKey    = @"Software\PEI Data Systems\PEI Site App";
#else
    internal const string DataDirName    = "PEI Site Service - Staging";
    internal const string RegistryKey    = @"Software\PEI Data Systems\PEI Site App - Staging";
#endif
}
