#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Save
{
    [Serializable]
    public sealed class SaveSnapshot
    {
        [SerializeField] private long revision;
        [SerializeField] private long confirmedCoins;
        [SerializeField] private WeaponInstance[] weapons;
        [SerializeField] private CatalogSaveRecord[] catalog;
        [SerializeField] private SettlementSaveRecord[] settlements;

        public SaveSnapshot(long revision, long confirmedCoins, IEnumerable<WeaponInstance> weapons,
            IEnumerable<CatalogSaveRecord> catalog, IEnumerable<SettlementSaveRecord> settlements)
        {
            this.revision = revision;
            this.confirmedCoins = confirmedCoins;
            this.weapons = (weapons ?? throw new ArgumentNullException(nameof(weapons))).ToArray();
            this.catalog = (catalog ?? throw new ArgumentNullException(nameof(catalog))).ToArray();
            this.settlements = (settlements ?? throw new ArgumentNullException(nameof(settlements))).ToArray();
            Validate();
        }

        public long Revision => revision;
        public long ConfirmedCoins => confirmedCoins;
        public IReadOnlyList<WeaponInstance> Weapons => Array.AsReadOnly(weapons);
        public IReadOnlyList<CatalogSaveRecord> Catalog => Array.AsReadOnly(catalog);
        public IReadOnlyList<SettlementSaveRecord> Settlements => Array.AsReadOnly(settlements);

        public bool TryGetSettlement(string transactionId, out SettlementSaveRecord? record)
        {
            foreach (SettlementSaveRecord settlement in settlements)
            {
                if (StringComparer.Ordinal.Equals(settlement.TransactionId, transactionId))
                {
                    record = settlement;
                    return true;
                }
            }

            record = null;
            return false;
        }

        internal void Validate()
        {
            if (revision <= 0)
                throw new InvalidDataException("A saved snapshot must have a positive revision.");

            if (weapons == null || catalog == null || settlements == null)
                throw new InvalidDataException("A snapshot is missing a required collection.");

            IReadOnlyList<WeaponInstanceIssue> weaponIssues = WeaponInstanceValidator.Validate(weapons);
            if (weaponIssues.Count != 0)
            {
                WeaponInstanceIssue issue = weaponIssues[0];
                throw new InvalidDataException($"Invalid weapon at index {issue.Index}: {issue.Kind}.");
            }

            var definitionIds = new HashSet<WeaponDefinitionId>();
            foreach (CatalogSaveRecord record in catalog)
            {
                if (record == null || !record.IsValid || !definitionIds.Add(record.DefinitionId))
                    throw new InvalidDataException("A catalog record is invalid or duplicates an existing definition ID.");
            }

            var runIds = new HashSet<Guid>();
            var transactionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementSaveRecord record in settlements)
            {
                if (record == null || !record.IsValid ||
                    !runIds.Add(record.RunId) || !transactionIds.Add(record.TransactionId))
                    throw new InvalidDataException("A settlement record is invalid or duplicates a run or transaction ID.");
            }
        }
    }

    [Serializable]
    public sealed class CatalogSaveRecord
    {
        [SerializeField] private string definitionId;
        [SerializeField] private string state;

        public CatalogSaveRecord(WeaponDefinitionId definitionId, string state)
        {
            this.definitionId = definitionId.Value;
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public WeaponDefinitionId DefinitionId => new WeaponDefinitionId(definitionId);
        public string State => state;
        internal bool IsValid => DefinitionId.IsValid && state != null;
    }

    [Serializable]
    public sealed class SettlementSaveRecord
    {
        [SerializeField] private string runId;
        [SerializeField] private string transactionId;
        [SerializeField] private string result;

        public SettlementSaveRecord(Guid runId, string transactionId, string result)
        {
            this.runId = runId.ToString("D");
            this.transactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
            this.result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public Guid RunId => Guid.TryParseExact(runId, "D", out Guid parsed) ? parsed : Guid.Empty;
        public string TransactionId => transactionId;
        public string Result => result;
        internal bool IsValid => RunId != Guid.Empty && !string.IsNullOrWhiteSpace(transactionId)
            && transactionId == transactionId.Trim() && result != null;
    }
}
