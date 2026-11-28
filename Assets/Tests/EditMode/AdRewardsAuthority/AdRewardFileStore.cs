#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.AdRewards.Tests
{
    /// <summary>Durable reference double with no in-memory ledger. Individual fixture instances share a writer lock.</summary>
    internal sealed class AdRewardFileStore : IAdRewardAuthorityStore
    {
        private readonly string path;
        private readonly object writerLock;
        private readonly Action? beforeActivation;

        internal AdRewardFileStore(string path, object writerLock, Action? beforeActivation = null)
        {
            this.path = path;
            this.writerLock = writerLock;
            this.beforeActivation = beforeActivation;
        }

        internal void Seed(AdRewardAuthoritySnapshot snapshot)
        {
            lock (writerLock)
                Write(snapshot);
        }

        public AdRewardAuthoritySnapshot Read()
        {
            lock (writerLock)
                return ReadSnapshot();
        }

        public bool TryCommit(long expectedRevision, AdRewardGrant grant)
        {
            lock (writerLock)
            {
                AdRewardAuthoritySnapshot active = ReadSnapshot();
                if (active.Revision != expectedRevision)
                    return false;

                var next = new AdRewardAuthoritySnapshot(active.AuthorityScope, checked(active.Revision + 1),
                    active.Reservations, active.Grants.Append(grant));
                Write(next);
                return true;
            }
        }

        private AdRewardAuthoritySnapshot ReadSnapshot()
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            string scope = reader.ReadString();
            long revision = reader.ReadInt64();
            var reservations = new AdRewardReservation[reader.ReadInt32()];
            for (int i = 0; i < reservations.Length; i++)
                reservations[i] = new AdRewardReservation(reader.ReadString(), reader.ReadString(), ReadTime(reader),
                    ReadTime(reader), reader.ReadBoolean());

            var grants = new AdRewardGrant[reader.ReadInt32()];
            for (int i = 0; i < grants.Length; i++)
            {
                var completion = new VerifiedAdRewardCompletion(scope, reader.ReadString(), reader.ReadString(), reader.ReadString());
                DateTime day = ReadTime(reader);
                if (!WeaponInstanceId.TryParse(reader.ReadString(), out WeaponInstanceId weaponId))
                    throw new InvalidDataException("The saved grant weapon identity is invalid.");
                WeaponInstance weapon = WeaponInstance.Rehydrate(weaponId, new WeaponDefinitionId(reader.ReadString()),
                    (WeaponProvenance)reader.ReadInt32(), WeaponLocation.Warehouse());
                grants[i] = new AdRewardGrant(completion, day, weapon);
            }
            return new AdRewardAuthoritySnapshot(scope, revision, reservations, grants);
        }

        private void Write(AdRewardAuthoritySnapshot snapshot)
        {
            string temporary = path + ".staging";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(snapshot.AuthorityScope);
                    writer.Write(snapshot.Revision);
                    writer.Write(snapshot.Reservations.Count);
                    foreach (AdRewardReservation reservation in snapshot.Reservations)
                    {
                        writer.Write(reservation.Token);
                        writer.Write(reservation.AccountId);
                        writer.Write(reservation.CreatedAtUtc.Ticks);
                        writer.Write(reservation.AcceptUntilUtc.Ticks);
                        writer.Write(reservation.AcceptsCompletion);
                    }
                    writer.Write(snapshot.Grants.Count);
                    foreach (AdRewardGrant grant in snapshot.Grants)
                    {
                        writer.Write(grant.Completion.AccountId);
                        writer.Write(grant.Completion.ReservationToken);
                        writer.Write(grant.Completion.ProviderEventId);
                        writer.Write(grant.ReservationDayUtc.Ticks);
                        writer.Write(grant.Weapon.Id.ToString());
                        writer.Write(grant.Weapon.DefinitionId.Value);
                        writer.Write((int)grant.Weapon.Provenance);
                    }
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                beforeActivation?.Invoke();
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static DateTime ReadTime(BinaryReader reader) => new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
    }
}
