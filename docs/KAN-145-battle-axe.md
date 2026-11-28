# KAN-145 전투도끼 기본공격

2등급 `battle_axe`는 `PrototypeBattleAxeWeaponCatalog`에 등록돼 있다. 기본공격은 기존 `MeleeSlashPattern` 에셋 하나인 `BattleAxeMeleeSlash`다. 검증값은 180° 부채꼴, 거리 2유닛, 대상당 피해 16, 재발동 1200ms다. 밸런스는 패턴 에셋에서 조정한다. [KAN-145](https://gametithe.atlassian.net/browse/KAN-145) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

자동공격이 가장 가까운 유효 대상을 선택하면 그 방향에서 발동 시각에 피해 묶음 하나를 만든다. 각도와 거리 경계를 포함하고, 같은 대상 ID는 한 발동에 한 번만 피해를 받는다. 검증용 카탈로그와 패턴에는 추가 스킬 및 최종 VFX 연결이 없다.

EditMode의 `ProjectWeapon.AutoAttack.Tests.BattleAxeTests`는 실제 카탈로그 에셋을 로드하여 스케줄러와 피해 판정을 호출한다. 양쪽 90° 경계, 거리 경계, 뒤쪽·사거리 밖·무효 대상, 중복 충돌 후보, 발동 시각과 재발동 간격을 확인한다.
