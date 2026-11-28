#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Catalog;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.CatalogUI
{
    internal enum WeaponSilhouette { Sword, Spear, Bow, Mace, Dagger, Axe, Scythe, TwinSwords }

    internal readonly struct CatalogEntry
    {
        internal CatalogEntry(string id, string name, int grade, WeaponSilhouette silhouette, string acquisition)
        {
            Id = new WeaponDefinitionId(id);
            Name = name;
            Grade = grade;
            Silhouette = silhouette;
            Acquisition = acquisition;
        }

        internal WeaponDefinitionId Id { get; }
        internal string Name { get; }
        internal int Grade { get; }
        internal WeaponSilhouette Silhouette { get; }
        internal string Acquisition { get; }
    }

    internal static class CatalogRoster
    {
        private const string Dungeon = "구역 탐험 · 코인/광고 뽑기";
        private const string CastleChest = "폐성 1구역 상자 · 코인/광고 뽑기";
        private const string UpperDungeon = "상위 구역 탐험 · 코인/광고 뽑기";

        internal static readonly IReadOnlyList<CatalogEntry> Entries = Array.AsReadOnly(new[]
        {
            new CatalogEntry("old_sword", "낡은 검", 1, WeaponSilhouette.Sword, Dungeon),
            new CatalogEntry("wooden_spear", "나무 창", 1, WeaponSilhouette.Spear, CastleChest),
            new CatalogEntry("hunting_bow", "사냥 활", 1, WeaponSilhouette.Bow, Dungeon),
            new CatalogEntry("mace", "철퇴", 1, WeaponSilhouette.Mace, Dungeon),
            new CatalogEntry("dagger", "단검", 1, WeaponSilhouette.Dagger, Dungeon),
            new CatalogEntry("flame_sword", "화염검", 2, WeaponSilhouette.Sword, Dungeon),
            new CatalogEntry("steel_spear", "강철창", 2, WeaponSilhouette.Spear, Dungeon),
            new CatalogEntry("repeater_bow", "연사궁", 2, WeaponSilhouette.Bow, Dungeon),
            new CatalogEntry("battle_axe", "전투도끼", 2, WeaponSilhouette.Axe, Dungeon),
            new CatalogEntry("twin_swords", "쌍검", 2, WeaponSilhouette.TwinSwords, Dungeon),
            new CatalogEntry("frost_spear", "서리창", 3, WeaponSilhouette.Spear, Dungeon),
            new CatalogEntry("lightning_sword", "번개검", 3, WeaponSilhouette.Sword, Dungeon),
            new CatalogEntry("blast_bow", "폭발궁", 3, WeaponSilhouette.Bow, Dungeon),
            new CatalogEntry("shadow_dagger", "그림자 단검", 3, WeaponSilhouette.Dagger, Dungeon),
            new CatalogEntry("lava_axe", "용암 도끼", 3, WeaponSilhouette.Axe, Dungeon),
            new CatalogEntry("storm_sword", "폭풍의 검", 4, WeaponSilhouette.Sword, Dungeon),
            new CatalogEntry("abyss_scythe", "심연의 낫", 4, WeaponSilhouette.Scythe, Dungeon),
            new CatalogEntry("sun_bow", "태양의 활", 4, WeaponSilhouette.Bow, Dungeon),
            new CatalogEntry("heavens_wrath_sword", "천벌의 검", 5, WeaponSilhouette.Sword, UpperDungeon),
            new CatalogEntry("doom_scythe", "종말의 낫", 5, WeaponSilhouette.Scythe, UpperDungeon),
            new CatalogEntry("genesis_bow", "창세의 활", 5, WeaponSilhouette.Bow, UpperDungeon),
        });

        internal static int CountRecovered(CatalogProgress progress) =>
            Entries.Count(entry => progress.GetStage(entry.Id) >= CatalogStage.Recovered);

        internal static string StageLabel(CatalogStage stage) => stage switch
        {
            CatalogStage.Discovered => "발견",
            CatalogStage.Recovered => "회수",
            CatalogStage.Mastered => "숙련",
            CatalogStage.Unrecognized => "기록 확인 필요",
            _ => "미발견"
        };
    }
}
