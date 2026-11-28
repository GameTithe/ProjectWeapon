#nullable enable

namespace ProjectWeapon.Inventory
{
    public enum StarterLoanStatus
    {
        InvalidRun = 0,
        Issued,
        AlreadyIssued,
        OwnedWeaponEquipped,
        InventoryNotPrepared,
        NoEquipmentSlot,
        InstanceIdConflict
    }

    public readonly struct StarterLoanResult
    {
        internal StarterLoanResult(StarterLoanStatus status, WeaponInstance? loan = null)
        {
            Status = status;
            Loan = loan;
        }

        public StarterLoanStatus Status { get; }

        /// <summary>The current instance, if it has not left the inventory.</summary>
        public WeaponInstance? Loan { get; }
    }

    public enum InventorySaleProvenance
    {
        InvalidInstance = 0,
        MissingInstance,
        ProvenanceRestricted,
        RequiresSaleRules
    }
}
