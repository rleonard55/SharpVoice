using System.Runtime.InteropServices;

namespace SharpVoice.Platform.Windows;

/// <summary>MSAAMENUINFO data for an owner-drawn menu, kept alive until its HMENU is destroyed.</summary>
public sealed unsafe class AccessibleMenuLabel : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Info { public uint Signature, Length; public char* Text; }
    public nint Data { get; private set; }
    public AccessibleMenuLabel(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Data = Marshal.AllocHGlobal(checked(sizeof(Info) + (text.Length + 1) * 2));
        var info = (Info*)Data;
        info->Signature = 0xAA0DF00D;
        info->Length = (uint)text.Length;
        info->Text = (char*)((byte*)Data + sizeof(Info));
        text.AsSpan().CopyTo(new Span<char>(info->Text, text.Length));
        info->Text[text.Length] = '\0';
    }
    public void Dispose()
    {
        if (Data == 0) return;
        Marshal.FreeHGlobal(Data); Data = 0;
    }
}
