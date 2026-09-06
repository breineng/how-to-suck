using System;
using UnityEngine;
namespace HowToSuck
{
    [Serializable] public sealed class AchievementCatalogEntry
    {
        public string Id,ApiName,Title,Description;
        public Sprite LockedIcon,UnlockedIcon;
    }
    [CreateAssetMenu(menuName="HowToSuck/Achievements")]
    public sealed class AchievementCatalog : ScriptableObject
    {
        public AchievementCatalogEntry[] Entries=Array.Empty<AchievementCatalogEntry>();
        public bool TryValidate(out string error)
        {
            error=null;
            if(Entries==null||Entries.Length!=8){error="Exactly eight authored achievements are required.";return false;}
            for(int i=0;i<8;i++)
            {
                var e=Entries[i];
                if(e==null||e.Id!=AchievementDefinitions.Id(i)||e.ApiName!=AchievementDefinitions.Api(i)||
                    string.IsNullOrWhiteSpace(e.Title)||string.IsNullOrWhiteSpace(e.Description)||e.LockedIcon==null||e.UnlockedIcon==null)
                {error="Achievement catalog identity, text or icon mismatch at "+i;return false;}
            }
            return true;
        }
    }
    public sealed class UnityAchievementProfileCodec : IAchievementProfileCodec
    {
        public string Encode(AchievementProfileStore store)=>JsonUtility.ToJson(store,true);
        public AchievementProfileStore Decode(string json)=>JsonUtility.FromJson<AchievementProfileStore>(json);
    }
}