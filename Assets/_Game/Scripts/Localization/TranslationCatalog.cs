using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HowToSuck
{
    // The source table is also the contract for legacy composed UI messages. Full templates take
    // precedence over fragments; captures retain numbers, keycap markup and their original formatting.
    public sealed class TranslationCatalog
    {
        [Serializable] public sealed class Table { public Entry[] entries; }
        [Serializable] public sealed class Entry { public string source; public string text; }
        private sealed class Pattern { public Regex regex; public string target; public int arguments; }
        private readonly Dictionary<string,string> exact = new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly List<Pattern> templates = new List<Pattern>();
        private readonly List<KeyValuePair<string,string>> fragments = new List<KeyValuePair<string,string>>();
        private static readonly Regex Token = new Regex(@"\{(\d+)\}", RegexOptions.CultureInvariant);
        public TranslationCatalog(IEnumerable<Entry> entries)
        {
            foreach (var entry in entries.OrderByDescending(e => e.source.Length))
            {
                if (string.IsNullOrEmpty(entry.source) || entry.text == null) continue;
                var tokens = Token.Matches(entry.source);
                if (tokens.Count == 0)
                {
                    exact[entry.source] = entry.text;
                    fragments.Add(new KeyValuePair<string,string>(entry.source,entry.text));
                    // Legacy writers include separators in their fragments. The same caption must
                    // still translate after a multiline message is split into individual lines.
                    string trimmed=entry.source.Trim();
                    if(trimmed.Length>0&&trimmed!=entry.source)
                        fragments.Add(new KeyValuePair<string,string>(trimmed,entry.text.Trim()));
                    continue;
                }
                var pattern = new StringBuilder("\\A"); int at = 0, arguments = 0;
                foreach (Match token in tokens)
                {
                    pattern.Append(Regex.Escape(entry.source.Substring(at, token.Index-at)));
                    int number = int.Parse(token.Groups[1].Value); arguments = Math.Max(arguments, number+1);
                    pattern.Append("(?<p").Append(number).Append(">.*?)"); at = token.Index+token.Length;
                }
                pattern.Append(Regex.Escape(entry.source.Substring(at))).Append("\\z");
                templates.Add(new Pattern { regex = new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(30)), target = entry.text, arguments = arguments });
            }
            fragments.Sort((a,b)=>b.Key.Length.CompareTo(a.Key.Length));
        }
        public static TranslationCatalog Load(string language)
        {
            var asset = Resources.Load<TextAsset>("Localization/"+language);
            if (asset == null) asset = Resources.Load<TextAsset>("Localization/english");
            return new TranslationCatalog(asset != null ? JsonUtility.FromJson<Table>(asset.text).entries : Array.Empty<Entry>());
        }
        public string Translate(string source) => Translate(source,0);
        private string Translate(string source,int depth)
        {
            if (string.IsNullOrEmpty(source) || depth > 3) return source ?? "";
            if (exact.TryGetValue(source,out var translated)) return translated;
            if (!source.Any(char.IsLetter)) return source;
            foreach (var template in templates)
            {
                Match match;
                try { match = template.regex.Match(source); } catch (RegexMatchTimeoutException) { continue; }
                if (!match.Success) continue;
                // Replace tokens explicitly: literal braces in UI text are never interpreted by string.Format.
                return Token.Replace(template.target,m => Translate(match.Groups["p"+m.Groups[1].Value].Value,depth+1));
            }
            // Existing HUD writers compose messages from independently authored lines and clauses.
            // Preserve separators while letting each complete numeric template translate as a unit.
            if(source.IndexOf('\n')>=0)return string.Join("\n",source.Split('\n').Select(line=>Translate(line,depth+1)));
            int clause=source.IndexOf(" · ",StringComparison.Ordinal);
            // A compound literal such as the charge-shot hint owns its internal separator.
            bool compoundLiteral=clause>0&&fragments.Any(part=>part.Key.IndexOf(" · ",StringComparison.Ordinal)>0&&source.Contains(part.Key));
            if(clause>0&&!compoundLiteral)return Translate(source.Substring(0,clause),depth+1)+Translate(source.Substring(clause),depth+1);
            var result = new StringBuilder(); int index = 0;
            while (index < source.Length)
            {
                // Rich-text attributes and key labels must never be translated.
                if (source[index]=='<')
                {
                    int end=source.IndexOf('>',index);
                    if(end>=0){result.Append(source,index,end-index+1);index=end+1;continue;}
                }
                KeyValuePair<string,string>? found = null;
                foreach (var part in fragments)
                {
                    if (part.Key.Length>source.Length-index || string.CompareOrdinal(source,index,part.Key,0,part.Key.Length)!=0) continue;
                    // Avoid replacing a short caption inside a longer word or a player-supplied name.
                    int end=index+part.Key.Length;
                    if(index>0&&char.IsLetter(source[index-1])&&char.IsLetter(part.Key[0]))continue;
                    if(end<source.Length&&char.IsLetter(source[end])&&char.IsLetter(part.Key[part.Key.Length-1]))continue;
                    found=part;break;
                }
                if(found.HasValue){result.Append(found.Value.Value);index+=found.Value.Key.Length;}
                else result.Append(source[index++]);
            }
            return result.ToString();
        }
    }
}
