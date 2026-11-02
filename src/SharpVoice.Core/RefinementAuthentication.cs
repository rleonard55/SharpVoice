namespace SharpVoice.Core;

public enum ChatGptAccountAction { None, SignIn, SignOut, ManageAccount, Reconnect }

public static class RefinementAuthentication
{
    public static ChatGptAccountAction ActionFor(string backend, bool signedIn) => backend == "gemini_antigravity" ? ChatGptAccountAction.ManageAccount : backend is not ("chatgpt_oauth" or "claude_code")
        ? ChatGptAccountAction.None : signedIn ? ChatGptAccountAction.ManageAccount : ChatGptAccountAction.SignIn;

    public static bool CanRequest(string backend, bool signedIn, bool busy, ChatGptAccountAction requested) =>
        !busy && (backend == "gemini_antigravity" ? requested is ChatGptAccountAction.Reconnect or ChatGptAccountAction.SignOut : backend is ("chatgpt_oauth" or "claude_code")
        && (signedIn ? requested is ChatGptAccountAction.Reconnect or ChatGptAccountAction.SignOut : requested == ChatGptAccountAction.SignIn));
}
