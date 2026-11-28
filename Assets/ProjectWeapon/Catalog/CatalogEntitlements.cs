#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Save;

namespace ProjectWeapon.Catalog
{
    public enum CatalogEntitlementKind
    {
        Appearance,
        Title,
        ArmoryDecoration
    }

    public readonly struct CatalogEntitlement
    {
        internal CatalogEntitlement(string id, CatalogEntitlementKind kind, int requiredRecoveredCount)
        {
            Id = id;
            Kind = kind;
            RequiredRecoveredCount = requiredRecoveredCount;
        }

        public string Id { get; }
        public CatalogEntitlementKind Kind { get; }
        public int RequiredRecoveredCount { get; }
    }

    /// <summary>Reads non-combat entitlements from committed catalog progress.</summary>
    public static class CatalogEntitlements
    {
        public const string AppearanceId = "catalog_recovery_18_appearance";
        public const string TitleId = "catalog_recovery_21_title";
        public const string ArmoryDecorationId = "catalog_recovery_21_armory_decoration";

        private const int AppearanceThreshold = 18;
        private const int FullCatalogThreshold = 21;

        private static readonly CatalogEntitlement Appearance =
            new CatalogEntitlement(AppearanceId, CatalogEntitlementKind.Appearance, AppearanceThreshold);
        private static readonly IReadOnlyList<CatalogEntitlement> AppearanceOnly = Array.AsReadOnly(new[] { Appearance });
        private static readonly IReadOnlyList<CatalogEntitlement> All = Array.AsReadOnly(new[]
        {
            Appearance,
            new CatalogEntitlement(TitleId, CatalogEntitlementKind.Title, FullCatalogThreshold),
            new CatalogEntitlement(ArmoryDecorationId, CatalogEntitlementKind.ArmoryDecoration, FullCatalogThreshold)
        });

        public static IReadOnlyList<CatalogEntitlement> Read(SaveSnapshot committedSnapshot)
        {
            if (committedSnapshot == null)
                throw new ArgumentNullException(nameof(committedSnapshot));

            int recoveredCount = new CatalogProgress(committedSnapshot.Catalog).RecoveredCount;
            if (recoveredCount >= FullCatalogThreshold)
                return All;
            return recoveredCount >= AppearanceThreshold ? AppearanceOnly : Array.Empty<CatalogEntitlement>();
        }
    }
}
