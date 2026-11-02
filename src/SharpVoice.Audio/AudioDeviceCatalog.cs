using System.Runtime.InteropServices;

namespace SharpVoice.Audio;

public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>Active capture endpoint IDs and friendly names, without runtime COM marshalling.</summary>
public static unsafe partial class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioDeviceInfo> Enumerate()
    {
        var result = new List<AudioDeviceInfo> { new("", "Windows default microphone") };
        var initialized = CoInitializeEx(0, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Check(initialized);
        nint enumerator = 0, collection = 0;
        try
        {
            var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            var iid = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
            Check(CoCreateInstance(in clsid, 0, 1, in iid, out enumerator));
            Check(((delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)Slot(enumerator, 3))(enumerator, 1, 1, &collection));
            uint count;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(collection, 3))(collection, &count));
            for (uint i = 0; i < count; i++)
            {
                nint device = 0, store = 0, id = 0;
                var value = new PropertyValue();
                try
                {
                    Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(collection, 4))(collection, i, &device));
                    Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(device, 5))(device, &id));
                    var endpoint = Marshal.PtrToStringUni(id) ?? throw new InvalidDataException("Microphone has no endpoint ID.");
                    Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(device, 4))(device, 0, &store));
                    var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                    Check(((delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropertyValue*, int>)Slot(store, 5))(store, &key, &value));
                    var name = value.Type == 31 ? Marshal.PtrToStringUni(value.Text) : null;
                    result.Add(new(endpoint, string.IsNullOrWhiteSpace(name) ? "Microphone " + (i + 1) : name));
                }
                finally { PropVariantClear(ref value); if (id != 0) Marshal.FreeCoTaskMem(id); Release(store); Release(device); }
            }
            return result;
        }
        finally { Release(collection); Release(enumerator); if (initialized >= 0) CoUninitialize(); }
    }

    private static nint Slot(nint obj, int index) => (*(nint**)obj)[index];
    private static void Release(nint obj) { if (obj != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropertyValue { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Text; }
    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [LibraryImport("ole32.dll")] private static partial int PropVariantClear(ref PropertyValue value);
}
