#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.OldSwordSkill
{
    public readonly struct OldSwordWideSlashStrike
    {
        internal OldSwordWideSlashStrike(ManualSkillActivationRequest activation, AreaDamageRequest area,
            IReadOnlyList<DamageRequest> hits)
        {
            RunId = activation.RunId;
            WeaponInstanceId = activation.Weapon.InstanceId;
            EffectiveAt = activation.StartedAt;
            Area = area;
            Hits = hits;
        }

        public RunId RunId { get; }
        public WeaponInstanceId WeaponInstanceId { get; }
        public TimeSpan EffectiveAt { get; }
        public AreaDamageRequest Area { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Old Sword Wide Slash")]
    public sealed class OldSwordWideSlashPattern : ManualSkillPattern
    {
        private static readonly WeaponDefinitionId OldSword = new WeaponDefinitionId("old_sword");

        [SerializeField, UnityEngine.Range(0f, 360f)] private float arcDegrees = 150f;
        [SerializeField, Min(0f)] private float damage = 26f;

        public bool TryCreateStrike(ManualSkillActivationRequest request,
            IReadOnlyList<TargetCandidate> candidates, out OldSwordWideSlashStrike strike)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            strike = default;
            if (request.Pattern != this || !request.Weapon.Definition || request.Weapon.Definition.Skill != this
                || !request.Weapon.Definition.Id.Equals(OldSword) || AimKind != ManualSkillAimKind.Direction
                || request.Aim.Kind != ManualSkillAimKind.Direction
                || !float.IsFinite(MaxAimDistance) || MaxAimDistance <= 0f
                || !float.IsFinite(arcDegrees) || arcDegrees <= 0f || arcDegrees > 360f
                || !float.IsFinite(damage) || damage <= 0f)
                return false;

            AreaDamageRequest area = AreaDamageRequest.Sector(request.GestureId, request.AttackerId, request.Origin,
                MaxAimDistance, damage, request.Aim.Vector, arcDegrees);
            strike = new OldSwordWideSlashStrike(request, area, AreaDamageSelector.FindTargets(area, candidates));
            return true;
        }
    }

    /// <summary>Resolves the slash synchronously against the live candidates supplied by the fight host.</summary>
    public sealed class OldSwordWideSlashEffect : IManualSkillEffect
    {
        private readonly Func<IReadOnlyList<TargetCandidate>> readCandidates;
        private readonly Action<OldSwordWideSlashStrike> applyStrike;

        public OldSwordWideSlashEffect(Func<IReadOnlyList<TargetCandidate>> readCandidates,
            Action<OldSwordWideSlashStrike> applyStrike)
        {
            this.readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
            this.applyStrike = applyStrike ?? throw new ArgumentNullException(nameof(applyStrike));
        }

        public bool TryStart(ManualSkillActivationRequest request)
        {
            if (request.Pattern is not OldSwordWideSlashPattern pattern
                || !pattern.TryCreateStrike(request, readCandidates(), out OldSwordWideSlashStrike strike))
                return false;

            applyStrike(strike);
            return true;
        }
    }
}
