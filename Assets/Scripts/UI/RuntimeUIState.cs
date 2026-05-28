using System;

public static class RuntimeUIState
{
    public static event Action StateChanged;

    public static bool IsSettingsMenuOpen { get; private set; }

    public static bool IsDebugOverlayVisible { get; private set; }

    public static bool BlocksGameplayInput => IsSettingsMenuOpen;

    public static void SetSettingsMenuOpen(bool isOpen)
    {
        if (IsSettingsMenuOpen == isOpen)
        {
            return;
        }

        IsSettingsMenuOpen = isOpen;
        StateChanged?.Invoke();
    }

    public static void ToggleSettingsMenu()
    {
        SetSettingsMenuOpen(!IsSettingsMenuOpen);
    }

    public static void SetDebugOverlayVisible(bool isVisible)
    {
        if (IsDebugOverlayVisible == isVisible)
        {
            return;
        }

        IsDebugOverlayVisible = isVisible;
        StateChanged?.Invoke();
    }

    public static void ToggleDebugOverlay()
    {
        SetDebugOverlayVisible(!IsDebugOverlayVisible);
    }
}
