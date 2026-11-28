#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.MaceSkill
{
    public readonly struct MaceShockwaveStrike
    {
        internal MaceShockwaveStrike(ManualSkillActivationRequest activation, AreaDamageRequest area,
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

    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Mace Shockwave")]
    public sealed class MaceShockwavePattern : ManualSkillPattern
    {
        private static readonly WeaponDefinitionId Mace = new WeaponDefinitionId("mace");

        [SerializeField, Min(0f)] private float radius = 1.9f;
        [SerializeField, Min(0f)] private float damage = 30f;

        public bool TryCreateStrike(ManualSkillActivationRequest request,
            IReadOnlyList<TargetCandidate> candidates, out MaceShockwaveStrike strike)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            strike = default;
            if (request.Pattern != this || !request.Weapon.Definition || request.Weapon.Definition.Skill != this
                || !request.Weapon.Definition.Id.Equals(Mace) || AimKind != ManualSkillAimKind.Self
                || request.Aim.Kind != ManualSkillAimKind.Self
                || !float.IsFinite(radius) || radius <= 0f || !float.IsFinite(damage) || damage <= 0f)
                return false;

            AreaDamageRequest area = AreaDamageRequest.Circle(request.GestureId, request.AttackerId,
                request.Origin, radius, damage);
            strike = new MaceShockwaveStrike(request, area, AreaDamageSelector.FindTargets(area, candidates));
            return true;
        }
    }

    /// <summary>Resolves one immediate shockwave against the fight host's current candidates.</summary>
    public sealed class MaceShockwaveEffect : IManualSkillEffect
    {
        private readonly Func<IReadOnlyList<TargetCandidate>> readCandidates;
        private readonly Action<MaceShockwaveStrike> applyStrike;

        public MaceShockwaveEffect(Func<IReadOnlyList<TargetCandidate>> readCandidates,
            Action<MaceShockwaveStrike> applyStrike)
        {
            this.readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
            this.applyStrike = applyStrike ?? throw new ArgumentNullException(nameof(applyStrike));
        }

        public bool TryStart(ManualSkillActivationRequest request)
        {
            if (request.Pattern is not MaceShockwavePattern pattern
                || !pattern.TryCreateStrike(request, readCandidates(), out MaceShockwaveStrike strike))
                return false;

            applyStrike(strike);
            return true;
        }
    }
}
