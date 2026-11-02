using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;
internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckTurnaround(NativeDesktop desktop,Action<ThemeColors> changeTheme,string? output)
    {
        var window=desktop.SettingsHandle;Win32.ShowWindow(window,4);var tabs=Win32.GetDlgItem(window,1014);Win32.SendMessageW(tabs,0x130C,1,0);
        var notice=new Win32.Notification{Window=tabs,Id=1014,Code=-551};Win32.SendMessageW(window,0x4E,1014,(nint)(&notice));
        var speed=Win32.GetDlgItem(window,1041);
        if(speed==0||Win32.SendMessageW(speed,0x146,0,0)!=2)throw new Exception("Speech processing choices are missing.");
        var backend=Win32.GetDlgItem(window,1002);Win32.SendMessageW(backend,0x14E,0,0);Win32.SendMessageW(window,0x111,(1u<<16)|1002,backend);
        Win32.SendMessageW(speed,0x14E,1,0);var previous=desktop.ConfigChanged;AppConfig? saved=null;desktop.ConfigChanged=value=>saved=value;
        Win32.SendMessageW(window,0x111,101,0);desktop.ConfigChanged=previous;
        if(saved?.FastTranscription!=true)throw new Exception("Apply Settings lost the speech-processing choice.");
        Win32.ShowWindow(window,4);desktop.SetTurnaround("Release to Insertion Dispatch: 850 ms\nTranscription: 650 ms");
        Win32.SendMessageW(window,0x111,2104,0);
        var text=new char[4096];fixed(char* p=text){var count=Win32.GetWindowTextW(desktop.HelpBody,p,text.Length);if(!new string(text,0,count).Contains("Transcription: 650 ms"))throw new Exception("Timing details were not available on demand.");}
        Win32.SendMessageW(window,0x111,2104,0);
        foreach(var palette in new[]{ThemeColors.Light,ThemeColors.DarkTheme})
        {changeTheme(palette);Win32.SendMessageW(window,0x1A,0,0);Pump();RenderHelp(window,output is null?null:output+(palette.Dark?"-turnaround-dark.bmp":"-turnaround-light.bmp"),60);}
        Console.WriteLine("PASS optional fast transcription choice, saved preference, on-demand timing and both themes.");
    }
}
