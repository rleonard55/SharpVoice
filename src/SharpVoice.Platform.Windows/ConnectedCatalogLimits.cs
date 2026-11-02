namespace SharpVoice.Platform.Windows;

// Discovery carries definitions for all configured tools, rather than an individual tool result.
internal static class ConnectedCatalogLimits
{
    internal const int ResponseCharacters=16*1_048_576;
    internal const int JsonDepth=128;
    internal const int Tools=SharpVoice.Core.AppConfig.MaximumConnectedTools;
    internal const int RetainedCharacters=16*1_048_576;
    internal const string TooLarge="The connected tool catalog exceeds SharpVoice's discovery budget. Disable unused integrations in Windows Codex, then retry discovery.";
}
