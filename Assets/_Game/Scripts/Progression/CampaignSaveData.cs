using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HowToSuck
{
    public enum SaveReadKind { Valid, Missing, Corrupt, FutureSchema, UnknownTier, IoError }
    public sealed class SaveReadResult
    {
        public SaveReadKind Kind {get;}
        public CampaignState State {get;}
        public string Error {get;}
        internal SaveReadResult(SaveReadKind kind,CampaignState state=null,string error=null)
        {Kind=kind;State=state;Error=error;}
    }
    // Explicit versioned JSON codec, not reflection deserialization of live campaign/services.
    public static class CampaignSaveData
    {
        public const int SchemaVersion=2,MaximumBytes=16384;
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        private static readonly string[] NamesV1={"schemaVersion","campaignId","balance","currentTierId","lastSettledRunId"};
        private static readonly string[] NamesV2=NamesV1.Concat(new[]{"purchasedExtraSlots","clearedContractIds","legacyContractAccess"}).ToArray();
        public static byte[] Encode(CampaignState value)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            var data=new JObject {
                ["schemaVersion"]=SchemaVersion,["campaignId"]=value.CampaignId,["balance"]=value.Balance,
                ["currentTierId"]=value.CurrentTierId,["lastSettledRunId"]=value.LastSettledRunId==null?JValue.CreateNull():new JValue(value.LastSettledRunId),
                ["purchasedExtraSlots"]=value.PurchasedExtraSlots,["clearedContractIds"]=new JArray(value.ClearedContractIds),["legacyContractAccess"]=value.LegacyContractAccess};
            return Utf8.GetBytes(data.ToString(Formatting.Indented)+"\n");
        }
        public static SaveReadResult Decode(byte[] bytes,CampaignTierCatalog catalog)
        {
            try {
                if(bytes==null||bytes.Length==0||bytes.Length>MaximumBytes)return Bad("Save size is invalid.");
                using(var text=new StringReader(Utf8.GetString(bytes)))
                using(var reader=new JsonTextReader(text){DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Decimal,MaxDepth=8})
                {
                    var token=JToken.ReadFrom(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error,CommentHandling=CommentHandling.Load});
                    while(reader.Read())if(reader.TokenType!=JsonToken.None)return Bad("Trailing JSON content is not allowed.");
                    if(!(token is JObject data))return Bad("Save must be an object.");
                    var schema=data["schemaVersion"];
                    if(schema==null||schema.Type!=JTokenType.Integer)return Bad("Integer schemaVersion is required.");
                    if(!long.TryParse(schema.ToString(),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var version))
                        return schema.ToString().StartsWith("-",StringComparison.Ordinal)?Bad("Invalid negative schemaVersion."):new SaveReadResult(SaveReadKind.FutureSchema,error:"Unsupported schemaVersion.");
                    // Version is inspected before any fallback/known-field validation: never restore older backup over a future file.
                    if(version>SchemaVersion)return new SaveReadResult(SaveReadKind.FutureSchema,error:"This campaign requires a newer game version.");
                    if(version!=1&&version!=2)return Bad("Unsupported old/invalid campaign schema.");
                    var names=version==1?NamesV1:NamesV2;
                    if(data.Properties().Count()!=names.Length||names.Any(n=>data.Property(n)==null))return Bad("Exact versioned campaign fields are required.");
                    foreach(string name in new[]{"campaignId","currentTierId"})if(data[name].Type!=JTokenType.String)return Bad(name+" must be a string.");
                    if(data["lastSettledRunId"].Type!=JTokenType.Null&&data["lastSettledRunId"].Type!=JTokenType.String)return Bad("lastSettledRunId must be null or a GUID.");
                    if(data["balance"].Type!=JTokenType.Integer||!long.TryParse(data["balance"].ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out var balance))
                        return Bad("balance must be a nonnegative Int64 integer.");
                    int bonus=0;string[] cleared=Array.Empty<string>();bool legacy=version==1;
                    if(version==2){
                        if(data["purchasedExtraSlots"].Type!=JTokenType.Integer||!int.TryParse(data["purchasedExtraSlots"].ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out bonus)||!CampaignCapacityRules.ValidBonus(bonus))return Bad("purchasedExtraSlots must be an integer 0..8.");
                        if(data["legacyContractAccess"].Type!=JTokenType.Boolean)return Bad("legacyContractAccess must be a boolean.");
                        legacy=(bool)data["legacyContractAccess"];
                        if(!(data["clearedContractIds"] is JArray history)||history.Count>64||history.Any(x=>x.Type!=JTokenType.String))return Bad("clearedContractIds must be a bounded array of stable IDs.");
                        cleared=history.Select(x=>(string)x).ToArray();
                    }
                    string tier=(string)data["currentTierId"];
                    var state=new CampaignState((string)data["campaignId"],tier,balance,
                        data["lastSettledRunId"].Type==JTokenType.Null?null:(string)data["lastSettledRunId"],bonus,cleared,legacy);
                    if(catalog==null||!catalog.Contains(tier))return new SaveReadResult(SaveReadKind.UnknownTier,error:"Saved tier is not in the current catalog: "+tier);
                    return new SaveReadResult(SaveReadKind.Valid,state);
                }
            }catch(Exception e) when(e is JsonException||e is ArgumentException||e is DecoderFallbackException||e is OverflowException)
            {return Bad("Invalid campaign JSON: "+e.Message);}
        }
        private static SaveReadResult Bad(string error)=>new SaveReadResult(SaveReadKind.Corrupt,error:error);
    }
}