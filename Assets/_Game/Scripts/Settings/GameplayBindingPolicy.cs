using System;
using System.Collections.Generic;
namespace HowToSuck
{
 public static class GameplayBindingPolicy
 {
  public sealed class Slot
  {
   public readonly string Id,Action,Label,DefaultPath,Part;
   public Slot(string id,string action,string label,string path,string part=null){Id=id;Action=action;Label=label;DefaultPath=path;Part=part;}
  }
  public static LocalBindingOverride[] Copy(LocalBindingOverride[] values)
  {if(values==null)return Array.Empty<LocalBindingOverride>();var result=new LocalBindingOverride[values.Length];for(int i=0;i<result.Length;i++)result[i]=values[i]?.Copy();return result;}
  public static bool Equal(LocalBindingOverride[] a,LocalBindingOverride[] b)
  {a=a??Array.Empty<LocalBindingOverride>();b=b??Array.Empty<LocalBindingOverride>();if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]==null||b[i]==null||a[i].Id!=b[i].Id||a[i].Path!=b[i].Path)return false;return true;}
  public static string PathFor(Slot[] slots,int slot,LocalBindingOverride[] values)
  {if(values!=null)foreach(var value in values)if(value!=null&&value.Id==slots[slot].Id)return value.Path;return slots[slot].DefaultPath;}
  public static bool SyntacticPath(string path)
  {
   if(string.IsNullOrEmpty(path)||path.Length>80)return false;
   string key;if(path.StartsWith("<Keyboard>/",StringComparison.OrdinalIgnoreCase))key=path.Substring(11);
   else if(path.StartsWith("<Mouse>/",StringComparison.OrdinalIgnoreCase)){
    key=path.Substring(8);return key.Equals("leftButton",StringComparison.OrdinalIgnoreCase)||key.Equals("rightButton",StringComparison.OrdinalIgnoreCase)||key.Equals("middleButton",StringComparison.OrdinalIgnoreCase)||key.Equals("forwardButton",StringComparison.OrdinalIgnoreCase)||key.Equals("backButton",StringComparison.OrdinalIgnoreCase);
   }else return false;
   if(key.Length==0||key.Equals("escape",StringComparison.OrdinalIgnoreCase)||key.Equals("anyKey",StringComparison.OrdinalIgnoreCase))return false;
   foreach(char c in key)if(!char.IsLetterOrDigit(c))return false;return true;
  }
  public static bool ValidateShape(LocalBindingOverride[] values)
  {
   if(values==null)return true;if(values.Length>32)return false;var ids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var value in values)if(value==null||!Guid.TryParseExact(value.Id,"D",out _)||!ids.Add(value.Id)||!SyntacticPath(value.Path))return false;return true;
  }
  public static bool Validate(Slot[] slots,LocalBindingOverride[] values,out string error)
  {
   error=null;if(slots==null||slots.Length==0||!ValidateShape(values)){error="Недопустимые привязки управления.";return false;}
   var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var slot in slots)ids.Add(slot.Id);
   if(values!=null)foreach(var value in values)if(!ids.Contains(value.Id)){error="Привязки созданы для другого набора действий. Сбросьте управление; Esc остаётся доступен.";return false;}
   var paths=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
   for(int i=0;i<slots.Length;i++){
    string path=PathFor(slots,i,values);if(paths.TryGetValue(path,out int prior)){error="Эта клавиша уже используется: "+slots[prior].Label+" / "+slots[i].Label+". Выберите другую.";return false;}
    paths.Add(path,i);
   }return true;
  }
  public static LocalBindingOverride[] With(Slot[] slots,LocalBindingOverride[] values,int index,string path)
  {var list=new List<LocalBindingOverride>();if(values!=null)foreach(var value in values)if(value.Id!=slots[index].Id)list.Add(value.Copy());if(!string.Equals(path,slots[index].DefaultPath,StringComparison.OrdinalIgnoreCase))list.Add(new LocalBindingOverride{Id=slots[index].Id,Path=path});list.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));return list.ToArray();}
 }
}
