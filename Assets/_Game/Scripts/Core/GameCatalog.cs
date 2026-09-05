using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    [CreateAssetMenu(menuName = "How to Suck/Game Catalog", fileName = "GameCatalog")]
    public sealed class GameCatalog : ScriptableObject
    {
        public ContractDefinition[] Contracts = Array.Empty<ContractDefinition>();
        public VacuumDefinition[] Vacuums = Array.Empty<VacuumDefinition>();

        public bool TryValidate(out string error)
        {
            if (Contracts == null || Contracts.Length == 0)
            {
                error = "Game catalog has no contracts. Assign at least one ready contract.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Contracts.Length; i++)
            {
                ContractDefinition contract = Contracts[i];
                if (contract == null)
                {
                    error = $"Game catalog contract at index {i} is missing.";
                    return false;
                }

                if (!contract.TryValidate(out error))
                    return false;

                if (!ids.Add(contract.ContractId))
                {
                    error = $"Game catalog contains duplicate contract ID '{contract.ContractId}'. Each contract needs a unique, stable ID.";
                    return false;
                }
            }

            if(Vacuums==null || Vacuums.Length==0) {error="Game catalog has no vacuum definitions.";return false;}
            ids.Clear();
            foreach(var vacuum in Vacuums)
            {
                if(vacuum==null) {error="Missing vacuum definition.";return false;}
                if(!vacuum.TryValidate(out error))return false;
                if(!ids.Add(vacuum.TierId)) {error="Duplicate vacuum tier: "+vacuum.TierId;return false;}
            }
            error = null;
            return true;
        }
    }
}
