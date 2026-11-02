using System.Text.Json;
using SharpVoice.Core;
namespace SharpVoice.Refiner;

public sealed partial class ConnectedConversation
{
    // Gateway keys always contain ::, so this local-only identifier cannot alias an integration.
    private const string CatalogSearchKey="__sharpvoice_find_tools";
    private static readonly ConnectedTool CatalogSearchTool=new("",CatalogSearchKey,"Search allowed tool names and descriptions locally. Use short keywords (e.g. gmail search). Returns up to two complete schemas; refine your query as needed.",ConnectedJson.Parse("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"],\"additionalProperties\":false}"),true,false);
    private ConnectedTool[] discoveredTools=[];
    private bool catalogSearch;
    private void SearchCatalog(JsonElement arguments)
    {
        if(arguments.ValueKind!=JsonValueKind.Object||arguments.EnumerateObject().Count()!=1||!arguments.TryGetProperty("query",out var value)||value.ValueKind!=JsonValueKind.String||value.GetString() is not {Length:>0 and <=256} query)
            throw new InvalidOperationException("Tool search requires a short query.");
        var terms=query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        if(terms.Length==0)throw new InvalidOperationException("Tool search requires a keyword.");
        var matches=gateway.Tools.Where(x=>allowed.Contains(x.Key)&&x.Supported)
            .Select(x=>(Tool:x,Score:terms.Sum(term=>(x.Key.Contains(term,StringComparison.OrdinalIgnoreCase)?3:0)+(x.Description.Contains(term,StringComparison.OrdinalIgnoreCase)?1:0))))
            .Where(x=>x.Score>0).OrderByDescending(x=>x.Score).ThenBy(x=>x.Tool.Key,StringComparer.Ordinal).Take(2).Select(x=>x.Tool).ToArray();
        discoveredTools=matches;
        // Metadata discovery never counts as retrieving account data or crosses the RPC gateway.
        history.Add("Local tool search: "+query+". Schemas now available: "+(matches.Length==0?"No matches. Try other integration/capability keywords.":string.Join(", ",matches.Select(x=>x.Key))));
        Log("Finding Available Tools · "+query);Show("Finding Available Tools");
    }
}
