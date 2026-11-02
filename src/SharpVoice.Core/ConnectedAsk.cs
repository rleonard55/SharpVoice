using System.Globalization;
using System.Text;
using System.Text.Json;
namespace SharpVoice.Core;

public sealed record ConnectedTool(string Server, string Name, string Description, JsonElement Schema, bool ReadOnly, bool Destructive)
{
    public string Key => Server + "::" + Name;
    public bool CanRead => ReadOnly && !Destructive && CreationLabel is null;
    public string? CreationLabel => !Destructive ? Name.Replace("__", "_").Replace('.', '_').Replace('/', '_') switch
    {
        "gmail_send_email" => "Send Email", "gmail_create_draft" => "Create Draft",
        "google_calendar_create_event" => "Create Event", "gcal_create_event" => "Create Event", _ => null
    } : null;
    public bool Supported => CanRead || CreationLabel is not null;
}
public sealed record ConnectedChange(Guid Id, string ToolKey, string Arguments, string Before, string After, string Label);
public sealed record ConnectedView(string Status, string Activity, string Before = "", string After = "", ConnectedChange? Change = null, string Answer = "", string Outcome = "");
public static class ConnectedJson
{
    public static JsonElement Parse(string text) { using var d=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=24});return d.RootElement.Clone(); }
    public static string Build(Action<Utf8JsonWriter> write)
    { using var bytes=new MemoryStream();using(var writer=new Utf8JsonWriter(bytes))write(writer);return Encoding.UTF8.GetString(bytes.ToArray()); }
    public static string Format(JsonElement value)
    {
        var text=new StringBuilder();Render(value,text,"",0);if(text.Length>64000)throw new InvalidOperationException("Tool data exceeds the complete display limit.");return text.ToString();
    }
    private static void Render(JsonElement value,StringBuilder text,string prefix,int depth)
    {
        if(depth>16||prefix.Length>4096||text.Length>64000)throw new InvalidOperationException("Tool data exceeds the complete display limit.");
        if(value.ValueKind==JsonValueKind.Object)
            foreach(var item in value.EnumerateObject())Render(item.Value,text,prefix+CultureInfo.InvariantCulture.TextInfo.ToTitleCase(item.Name.Replace('_',' '))+": ",depth+1);
        else if(value.ValueKind==JsonValueKind.Array)
        {var n=0;foreach(var item in value.EnumerateArray())Render(item,text,prefix+"["+(++n)+"] ",depth+1);if(n==0)text.AppendLine(prefix+"(None)");}
        else text.AppendLine(prefix+(value.ValueKind==JsonValueKind.String?value.GetString():value.GetRawText()));
    }
}
public static class ConnectedPolicy
{
    public static ConnectedChange Preview(ConnectedTool tool, JsonElement arguments)
    {
        var label=tool.CreationLabel??throw new InvalidOperationException("This change needs a verified before-state adapter. Nothing was executed.");
        ValidateArguments(tool.Schema,arguments);
        var raw=arguments.GetRawText();
        if(raw.Length>32000 || raw.Any(c=>c=='\0'||c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069'))throw new InvalidOperationException("Action content cannot be displayed safely and completely.");
        CheckReadable(arguments);
        ValidateCreation(tool,arguments);
        foreach(var field in arguments.EnumerateObject())
        {
            var key=field.Name.ToLowerInvariant();
            if(new[]{"raw","mime","base64","encoded","script"}.Any(key.Contains))throw new InvalidOperationException("Encoded action content cannot receive a complete readable review.");
            if(key is "to" or "cc" or "bcc" or "subject" or "in_reply_to" or "thread_id" && ContainsLineBreak(field.Value))throw new InvalidOperationException("Recipient and subject fields must not contain line breaks.");
        }
        var before=label=="Send Email"?"Not Sent — No Outgoing Message Exists":label=="Create Draft"?"No Draft Created":"No Event Created";
        var after=ConnectedJson.Format(arguments);
        if(label is "Send Email" or "Create Draft")
        {foreach(var name in new[]{"cc","bcc","attachments"})if(!arguments.TryGetProperty(name,out _))after+=CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name)+": (None)\r\n";}
        else if(!arguments.TryGetProperty("attendees",out _))after+="Attendees: (None)\r\n";
        return new(Guid.NewGuid(),tool.Key,raw,before,after,label);
    }
    private static bool ContainsLineBreak(JsonElement value)=>value.ValueKind==JsonValueKind.String&&value.GetString()!.IndexOfAny(['\r','\n'])>=0 || value.ValueKind==JsonValueKind.Array&&value.EnumerateArray().Any(ContainsLineBreak);
    private static void CheckReadable(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.String && value.GetString()!.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t')||c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069'))throw new InvalidOperationException("Action content contains hidden formatting controls.");
        if(value.ValueKind==JsonValueKind.Object)foreach(var field in value.EnumerateObject())
        {
            if(field.Name.Any(c=>char.IsControl(c)||c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069'))throw new InvalidOperationException("Action field names contain hidden formatting controls.");
            if(new[]{"raw","mime","base64","encoded","script"}.Any(field.Name.ToLowerInvariant().Contains))throw new InvalidOperationException("Opaque action fields cannot be reviewed.");
            CheckReadable(field.Value);
        }
        if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())CheckReadable(item);
    }
    private static void ValidateCreation(ConnectedTool tool,JsonElement arguments)
    {
        var email=tool.CreationLabel is "Send Email" or "Create Draft";
        var supported=email?new[]{"to","cc","bcc","subject","body","body_format","attachments","thread_id","in_reply_to"}:new[]{"calendar_id","summary","start","end","description","location","attendees"};
        if(!tool.Schema.TryGetProperty("properties",out var properties))throw new InvalidOperationException("No verified creation schema.");
        foreach(var field in properties.EnumerateObject())
        {
            if(!supported.Contains(field.Name,StringComparer.Ordinal))throw new InvalidOperationException("This creation schema has fields without a verified review adapter.");
            if(field.Value.TryGetProperty("default",out _)&&!arguments.TryGetProperty(field.Name,out _))throw new InvalidOperationException("Service defaults must be explicit in the reviewed action.");
        }
        string Required(string name)
        {if(!arguments.TryGetProperty(name,out var v)||v.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(v.GetString()))throw new InvalidOperationException("The reviewed action requires an explicit "+name+".");return v.GetString()!;}
        if(email)
        {
            Required("subject");Required("body");
            bool Recipient(JsonElement value,bool required)
            {
                if(value.ValueKind==JsonValueKind.Array)return (!required||value.GetArrayLength()>0)&&value.EnumerateArray().All(x=>Recipient(x,true));
                if(value.ValueKind!=JsonValueKind.String)return false;var text=value.GetString()!;
                if(string.IsNullOrWhiteSpace(text))return !required;
                try{var addresses=new System.Net.Mail.MailAddressCollection();addresses.Add(text);return addresses.Count>0&&addresses.All(x=>x.Address.Contains('@'));}catch(FormatException){return false;}
            }
            foreach(var name in new[]{"to","cc","bcc"})if(arguments.TryGetProperty(name,out var recipients)&&!Recipient(recipients,name=="to"))throw new InvalidOperationException("Use explicit email addresses for "+name+".");
            if(!arguments.TryGetProperty("to",out var to)||!(to.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(to.GetString())||to.ValueKind==JsonValueKind.Array&&to.GetArrayLength()>0&&to.EnumerateArray().All(x=>x.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(x.GetString()))))throw new InvalidOperationException("Choose explicit email recipients.");
            if(arguments.TryGetProperty("body_format",out var format)&&format.GetString() is not ("plain" or "text" or "text/plain"))throw new InvalidOperationException("Only plain-text outgoing messages have a verified readable review.");
            if(arguments.TryGetProperty("attachments",out var attachments)&&(attachments.ValueKind!=JsonValueKind.Array||attachments.GetArrayLength()!=0))throw new InvalidOperationException("Attachment metadata cannot yet be verified. Send without attachments or use your mail app.");
        }
        else
        {
            Required("calendar_id");Required("summary");
            if(arguments.TryGetProperty("attendees",out var attendees))
            {
                if(attendees.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Event attendees must be explicit email addresses.");
                foreach(var attendee in attendees.EnumerateArray())
                {
                    var address=attendee.ValueKind==JsonValueKind.String?attendee.GetString():attendee.ValueKind==JsonValueKind.Object&&attendee.TryGetProperty("email",out var attendeeEmail)&&attendeeEmail.ValueKind==JsonValueKind.String?attendeeEmail.GetString():null;
                    if(address is null||!System.Net.Mail.MailAddress.TryCreate(address,out var parsedAddress)||!parsedAddress.Address.Contains('@')||attendee.ValueKind==JsonValueKind.Object&&attendee.EnumerateObject().Any(x=>x.Name is not ("email" or "displayName" or "optional")))throw new InvalidOperationException("Event attendee identity or fields cannot be verified completely.");
                }
            }

            DateTimeOffset Date(string name)
            {
                if(!arguments.TryGetProperty(name,out var value)||value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty("dateTime",out var date)||date.ValueKind!=JsonValueKind.String||!value.TryGetProperty("timeZone",out var zone)||zone.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(zone.GetString())||value.EnumerateObject().Any(x=>x.Name is not ("dateTime" or "timeZone")))throw new InvalidOperationException("Calendar start/end require explicit dateTime and timeZone fields.");
                var text=date.GetString()!;
                if(!(text.EndsWith('Z')||text.Length>=6&&text[^6] is '+' or '-'&&text[^3]==':')||!DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))throw new InvalidOperationException("Calendar dates require explicit UTC offsets.");
                return parsed;
            }
            if(Date("end")<=Date("start"))throw new InvalidOperationException("Event end must follow its start.");
        }
    }
    public static void ValidateArguments(JsonElement schema,JsonElement value,int depth=0)
    {
        if(depth>16)throw new InvalidOperationException("Action schema is too complex.");
        if(schema.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("No inspectable tool schema is available.");
        if(schema.TryGetProperty("$ref",out _))throw new InvalidOperationException("Referenced tool schemas are not supported for reviewed actions.");
        if(schema.TryGetProperty("anyOf",out var union)||schema.TryGetProperty("oneOf",out union))
        {foreach(var branch in union.EnumerateArray())try{ValidateArguments(branch,value,depth+1);return;}catch(InvalidOperationException){}throw new InvalidOperationException("Tool argument does not match its schema.");}
        if(schema.TryGetProperty("enum",out var values)&&!values.EnumerateArray().Any(x=>x.GetRawText()==value.GetRawText()))throw new InvalidOperationException("Unsupported action value.");
        bool Matches(string? type)=>type switch{"object"=>value.ValueKind==JsonValueKind.Object,"array"=>value.ValueKind==JsonValueKind.Array,"string"=>value.ValueKind==JsonValueKind.String,"boolean"=>value.ValueKind is JsonValueKind.True or JsonValueKind.False,"number"=>value.ValueKind==JsonValueKind.Number,"integer"=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out _),"null"=>value.ValueKind==JsonValueKind.Null,_=>false};
        if(schema.TryGetProperty("type",out var type)&&!(type.ValueKind==JsonValueKind.Array?type.EnumerateArray().Any(x=>Matches(x.GetString())):Matches(type.GetString())))throw new InvalidOperationException("Tool argument has the wrong type.");
        if(value.ValueKind==JsonValueKind.Object)
        {
            if(!schema.TryGetProperty("properties",out var props)||props.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("Action fields cannot be verified.");
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var field in value.EnumerateObject())
            {if(!keys.Add(field.Name)||!props.TryGetProperty(field.Name,out var child))throw new InvalidOperationException("Unknown or duplicate action field.");ValidateArguments(child,field.Value,depth+1);}
            if(schema.TryGetProperty("required",out var required)&&required.EnumerateArray().Any(x=>!keys.Contains(x.GetString()!)))throw new InvalidOperationException("Required action field is missing.");
            if(props.EnumerateObject().Any(x=>x.Value.TryGetProperty("default",out _)&&!keys.Contains(x.Name)))throw new InvalidOperationException("Schema defaults must be explicit in the reviewed action.");
        }
        if(value.ValueKind==JsonValueKind.Array)
        {if(value.GetArrayLength()>1000||!schema.TryGetProperty("items",out var items))throw new InvalidOperationException("Action list cannot be reviewed.");foreach(var item in value.EnumerateArray())ValidateArguments(items,item,depth+1);}
    }
}
public sealed class ConnectedApproval
{
    private readonly object gate=new();private ConnectedChange? pending;
    public void Offer(ConnectedChange change){lock(gate)pending=change;}
    public bool Confirm(Guid id,string arguments)
    {lock(gate){if(pending is not{} p||p.Id!=id||p.Arguments!=arguments)return false;pending=null;return true;}}
    public void Cancel(){lock(gate)pending=null;}
}
public sealed record ConnectedPrompt(string Instructions,string Input);
public sealed record ConnectedStep(string Kind,string Tool,JsonElement Arguments,string Text)
{
    public static ConnectedStep Parse(string text)
    {
        if(text.Length>64000)throw new InvalidDataException("Connected response is too large.");
        var root=ConnectedJson.Parse(text);if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Expected a connected step object.");
        var props=root.EnumerateObject().ToArray();
        if(props.Length!=4||props.Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=4||props.Any(x=>x.Name is not ("kind" or "tool" or "arguments" or "text")))throw new InvalidDataException("Malformed connected step.");
        var kind=root.GetProperty("kind").GetString()!;var tool=root.GetProperty("tool").GetString()!;var args=root.GetProperty("arguments");var answer=root.GetProperty("text").GetString()!;
        if(kind is not ("tool" or "answer" or "question")||args.ValueKind!=JsonValueKind.Object||tool.Length>512||answer.Length>32000||args.GetRawText().Length>32000)throw new InvalidDataException("Invalid connected step values.");
        if(kind=="tool"?(tool.Length==0||answer.Length!=0):(tool.Length!=0||args.EnumerateObject().Any()||string.IsNullOrWhiteSpace(answer)))throw new InvalidDataException("Mixed tool and answer content is not allowed.");
        return new(kind,tool,args.Clone(),answer);
    }
}
