using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private void AccountMenu()
    {
        var backend = SelectedChoice("Backend");
        if (IsAuthenticationBusy?.Invoke() == true || RefinementAuthentication.ActionFor(backend, AccountSignedIn(backend)) != ChatGptAccountAction.ManageAccount) return;
        if (Win32.GetWindowRect(signOutButton, out var rect) == 0) throw new InvalidOperationException("Could not position the account menu.");
        ApplyTheme();
        var menu = Win32.CreatePopupMenu();
        if (menu == 0) throw new InvalidOperationException("Could not create account menu.");
        ClearMenuLabels();
        try
        {
            StyleMenu(menu);
            AddMenu(menu, 201, backend == "gemini_antigravity" ? "Open Google sign-in" : "Sign in again");
            AddMenu(menu, 202, backend == "gemini_antigravity" ? "Sign out in Antigravity" : "Sign out");
            var command = Win32.TrackPopupMenu(menu, 0x102, rect.Left, rect.Bottom + 4, 0, settingsWindow, 0);
            Win32.PostMessageW(settingsWindow, 0, 0, 0);
            if (command == 201) RequestAuthentication(ChatGptAccountAction.Reconnect, backend);
            else if (command == 202) RequestAuthentication(ChatGptAccountAction.SignOut, backend);
        }
        finally { Win32.DestroyMenu(menu); ClearMenuLabels(); }
    }
}
