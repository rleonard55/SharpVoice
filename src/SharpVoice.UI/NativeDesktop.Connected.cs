using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
namespace SharpVoice.UI;
public sealed partial class NativeDesktop
{
    public Action<Guid>? ConnectedConfirmRequested {get;set;}
    public Action? ConnectedCancelRequested {get;set;}
    public Action? ConnectedClosed {get;set;}
    public Action<string>? ConnectedQuestionRequested {get;set;}
    public Action? ConnectedVoiceRequested {get;set;}
    public Action? ConnectedReadRequested {get;set;}
    public Action? ConnectedNewRequested {get;set;}
    private nint connectedWindow,connectedStatus,connectedActivity,connectedBefore,connectedAfter,connectedInput,connectedConfirm,connectedAsk,connectedVoice,connectedRead;
    private readonly List<nint> connectedControls=[];private readonly Dictionary<nint,int> connectedButtons=[];
    private ConnectedChange? connectedPreview;private ConnectedView? connectedLastView;private bool connectedWriteConfirmed;private string connectedSources="";
    public nint ConnectedHandle=>connectedWindow;
    public bool ConnectedVisible=>connectedWindow!=0&&Win32.IsWindowVisible(connectedWindow)!=0;
    public unsafe void ShowConnected(ConnectedView view)
    {
        if(connectedWindow==0)
        {
            Win32.SystemParametersInfoW(0x30,0,out var work,0);
            connectedWindow=Create(0,"SharpVoice.Native","SharpVoice · Connected Ask AI",0x00CA0000,Math.Max(work.Left,(work.Right+work.Left-860)/2),Math.Max(work.Top,(work.Bottom+work.Top-750)/2),860,750,0,0);
            nint Item(string kind,string label,uint style,int x,int y,int width,int height,int id)
            {
                var c=Create(0,kind,label,0x50000000|style,x,y,width,height,connectedWindow,id);Win32.SendMessageW(c,0x30,(nuint)font,1);connectedControls.Add(c);InstallControlHover(c,kind);
                if(kind=="EDIT"){Win32.SendMessageW(c,0xD3,3,(8<<16)|8);Win32.SendMessageW(c,0xC5,262144,0);}
                if(kind=="BUTTON"){connectedButtons[c]=id;if(Win32.SetWindowSubclass(c,&ConnectedButtonProcedure,6,0)==0)throw new InvalidOperationException("Could not initialize connected review controls.");}
                return c;
            }
            var title=Item("STATIC","SharpVoice",0,24,18,790,34,4614);Win32.SendMessageW(title,0x30,(nuint)headingFont,1);
            Item("BUTTON","New Conversation",0x1000B,604,18,210,36,4617);
            connectedStatus=Item("STATIC","",0,24,60,790,42,4600);
            Item("STATIC","Activity, Retrieved Sources and Answer",0,24,106,790,24,4615);
            connectedActivity=Item("EDIT","",0x00A11844,24,134,790,170,4601);
            Item("STATIC","Before",0,24,320,382,24,4609);Item("STATIC","After · Proposed Change (+)",0,428,320,386,24,4610);
            connectedBefore=Item("EDIT","",0x00A11844,24,350,382,190,4602);connectedAfter=Item("EDIT","",0x00A11844,428,350,386,190,4603);
            Item("STATIC","Follow-Up Question",0,24,554,790,24,4616);connectedInput=Item("EDIT","",0x00A11044,24,581,604,52,4606);
            connectedAsk=Item("BUTTON","Ask",0x1000B,644,581,170,36,4607);
            connectedVoice=Item("BUTTON","Speak Follow-Up",0x1000B,24,649,176,36,4608);
            connectedRead=Item("BUTTON","Read Answer",0x1000B,210,649,145,36,4611);
            Item("BUTTON","Sources",0x1000B,365,649,104,36,4613);
            Item("BUTTON","Cancel",0x1000B,479,649,100,36,4604);
            connectedConfirm=Item("BUTTON","Review Required",0x1000B,589,649,225,36,4605);
        }
        connectedLastView=view;
        if(view.Change is not null||view.Status.StartsWith("Connecting",StringComparison.Ordinal)||view.Status=="Change Confirmed by Service")connectedWriteConfirmed=false;
        connectedPreview=view.Change;connectedSources=view.Activity;
        Win32.SetWindowTextW(connectedStatus,NativeConnectedText(view.Status+(view.Outcome.Length==0?"":"\r\n"+view.Outcome)));
        var content=view.Activity+(view.Answer.Length==0?"":"\r\nANSWER\r\n"+view.Answer);
        Win32.SetWindowTextW(connectedActivity,NativeConnectedText(content));Win32.SetWindowTextW(connectedBefore,NativeConnectedText(view.Before));Win32.SetWindowTextW(connectedAfter,NativeConnectedText(view.After));
        Win32.SetWindowTextW(connectedConfirm,view.Change?.Label??"No Pending Change");Win32.EnableWindow(connectedConfirm,view.Change is null?0:1);
        var ready=ConnectedReady(view.Status);
        Win32.EnableWindow(connectedAsk,ready?1:0);Win32.EnableWindow(connectedInput,ready?1:0);Win32.EnableWindow(connectedVoice,ready?1:0);Win32.EnableWindow(connectedRead,ready&&view.Answer.Length>0?1:0);
        Win32.SetWindowTextW(connectedVoice,"Speak Follow-Up");ApplyTheme();ApplyMicrophoneIcon();Win32.ShowWindow(connectedWindow,4);
    }
    private static string NativeConnectedText(string value)=>value.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Replace("\n","\r\n",StringComparison.Ordinal);
    public void FinishConnectedCancellation()
    {
        if(connectedLastView is not{} view||!ConnectedVisible)return;
        var uncertain=connectedWriteConfirmed||view.Status.StartsWith("Executing",StringComparison.Ordinal)||view.Outcome.Contains("Awaiting a Service Receipt",StringComparison.Ordinal);
        ShowConnected(view with { Status=uncertain?"Outcome Unknown · Check the Service Before Repeating":"Canceled",Change=null,Answer="",Outcome=uncertain?"A Confirmed Change May Have Reached the Service":view.Outcome });
    }
    public void SetConnectedStatus(string text){if(connectedStatus!=0)Win32.SetWindowTextW(connectedStatus,text);}
    private static bool ConnectedReady(string status)=>status is "Ready" or "Answered" or "Needs Clarification"||status.StartsWith("Canceled",StringComparison.Ordinal)||status.StartsWith("Request Failed",StringComparison.Ordinal)||status.StartsWith("Outcome Unknown",StringComparison.Ordinal);
    public void SetConnectedRecording(bool recording){if(connectedVoice==0)return;Win32.SetWindowTextW(connectedVoice,recording?"Stop Recording":"Speak Follow-Up");Win32.EnableWindow(connectedVoice,recording||connectedLastView is{} view&&ConnectedReady(view.Status)?1:0);}
    public void HideConnected()
    {
        if(!ConnectedVisible)return;connectedPreview=null;Win32.ShowWindow(connectedWindow,0);ConnectedClosed?.Invoke();
        connectedLastView=null;connectedWriteConfirmed=false;connectedSources="";Win32.SetWindowTextW(connectedActivity,"");Win32.SetWindowTextW(connectedBefore,"");Win32.SetWindowTextW(connectedAfter,"");Win32.SetWindowTextW(connectedInput,"");
    }
    private bool HandleConnectedMessage(nint hwnd,uint message,nuint wParam)
    {
        if(hwnd==0||hwnd!=connectedWindow)return false;
        if(message==0x10){HideConnected();return true;}
        if(message!=0x111||wParam>>16!=0)return false;
        switch((int)(wParam&0xFFFF))
        {
            case 2:HideConnected();break;
            case 4605:
                if(connectedPreview is{} change){connectedWriteConfirmed=true;connectedPreview=null;Win32.EnableWindow(connectedConfirm,0);SetConnectedStatus("Confirming Reviewed Change…");ConnectedConfirmRequested?.Invoke(change.Id);}break;
            case 4604:connectedPreview=null;Win32.EnableWindow(connectedConfirm,0);ConnectedCancelRequested?.Invoke();break;
            case 4607:if(connectedPreview is null&&HoverIsWindowEnabled(connectedAsk)!=0){var text=Text(connectedInput);if(!string.IsNullOrWhiteSpace(text)){ConnectedQuestionRequested?.Invoke(text);Win32.SetWindowTextW(connectedInput,"");}}break;
            case 4608:if(connectedPreview is null&&HoverIsWindowEnabled(connectedVoice)!=0)ConnectedVoiceRequested?.Invoke();break;
            case 4611:if(connectedPreview is null&&HoverIsWindowEnabled(connectedRead)!=0)ConnectedReadRequested?.Invoke();break;
            case 4613:OpenConnectedSource();break;
            case 4617:ConnectedNewRequested?.Invoke();break;
        }
        return true;
    }
    private void OpenConnectedSource()
    {
        var urls=Regex.Matches(connectedSources,@"https://[^\s<>""']+",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(x=>x.Value.TrimEnd('.',',',')',']')).Where(x=>Uri.TryCreate(x,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&!uri.IsLoopback).Distinct().Take(20).ToArray();
        if(urls.Length==0){SetConnectedStatus("No Source Links Were Returned · Retrieved Text Is Above");return;}
        var menu=Win32.CreatePopupMenu();try{for(var i=0;i<urls.Length;i++)Win32.AppendMenuW(menu,0,(nuint)(i+1),urls[i]);Win32.GetWindowRect(connectedRead,out var rect);var choice=Win32.TrackPopupMenu(menu,0x102,rect.Left,rect.Bottom,0,connectedWindow,0);if(choice>=1&&choice<=urls.Length)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(urls[choice-1]){UseShellExecute=true});}finally{Win32.DestroyMenu(menu);}
    }
    private bool HandleConnectedInput(in Win32.Msg message)
    {
        if(!connectedButtons.TryGetValue(message.Hwnd,out var id)||message.Message is not (0x100 or 0x101)||message.WParam is not (13 or 32))return false;
        if(message.Message==0x100&&((long)message.LParam&(1L<<30))==0&&HoverIsWindowEnabled(message.Hwnd)!=0)Win32.SendMessageW(connectedWindow,0x111,(nuint)id,message.Hwnd);return true;
    }
    [UnmanagedCallersOnly(CallConvs=[typeof(CallConvStdcall)])]
    private static unsafe nint ConnectedButtonProcedure(nint hwnd,uint message,nuint wParam,nint lParam,nuint id,nuint data)
    {
        if(current is{} self){var input=new Win32.Msg{Hwnd=hwnd,Message=message,WParam=wParam,LParam=lParam};if(self.HandleConnectedInput(input))return 0;if(message==0x87&&wParam is 13 or 32)return Win32.DefSubclassProc(hwnd,message,wParam,lParam)|4;}
        if(message==0x82)Win32.RemoveWindowSubclass(hwnd,&ConnectedButtonProcedure,id);return Win32.DefSubclassProc(hwnd,message,wParam,lParam);
    }
}
