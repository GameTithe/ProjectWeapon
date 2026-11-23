using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectWeapon.Dungeon
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Dungeon/Zone Catalog")]
    public sealed class ZoneCatalog : ScriptableObject
    {
        [SerializeField] private ZoneDefinition[] zones;

        public IReadOnlyList<ZoneDefinition> GetZones()
        {
            Validate();
            return Array.AsReadOnly(zones);
        }

        public ZoneDefinition GetZone(int zoneId)
        {
            if (zoneId <= 0)
                throw new ArgumentOutOfRangeException(nameof(zoneId), zoneId, "Zone ID must be positive.");

            Validate();
            foreach (ZoneDefinition zone in zones)
            {
                if (zone.ZoneId == zoneId)
                    return zone;
            }

            throw new KeyNotFoundException($"Zone {zoneId} is absent from catalog '{name}'.");
        }

        public void Validate()
        {
            if (zones == null || zones.Length == 0)
                throw new InvalidOperationException($"Zone catalog '{name}' must contain at least one zone.");

            var ids = new HashSet<int>();
            for (int index = 0; index < zones.Length; index++)
            {
                ZoneDefinition zone = zones[index];
                if (zone == null)
                    throw new InvalidOperationException($"Zone catalog '{name}' contains an empty entry at index {index}.");

                zone.Validate();
                if (!ids.Add(zone.ZoneId))
                    throw new InvalidOperationException($"Zone catalog '{name}' contains duplicate zone ID {zone.ZoneId}.");
            }
        }
    }
}
