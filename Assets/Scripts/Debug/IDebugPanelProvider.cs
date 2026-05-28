using System.Collections.Generic;

public interface IDebugPanelProvider
{
    int DebugSortOrder { get; }

    string DebugSectionTitle { get; }

    bool ShouldDisplayInDebugOverlay { get; }

    void AppendDebugLines(List<string> lines);
}

public static class DebugPanelRegistry
{
    private static readonly List<IDebugPanelProvider> Providers = new List<IDebugPanelProvider>();

    public static IReadOnlyList<IDebugPanelProvider> RegisteredProviders => Providers;

    public static void Register(IDebugPanelProvider provider)
    {
        if (provider == null || Providers.Contains(provider))
        {
            return;
        }

        Providers.Add(provider);
        Providers.Sort((left, right) => left.DebugSortOrder.CompareTo(right.DebugSortOrder));
    }

    public static void Unregister(IDebugPanelProvider provider)
    {
        if (provider == null)
        {
            return;
        }

        Providers.Remove(provider);
    }
}
