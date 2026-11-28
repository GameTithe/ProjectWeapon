# KAN-68 폐성 원거리 적 행동

제자리에 있는 원거리 적은 플레이어가 4유닛 이내로 들어오면 직선 공격을 예고한다. 예고 시작 순간의 위치와 방향을 고정해 750ms 뒤 길이 4유닛, 폭 0.7유닛의 같은 직사각형으로 한 번 타격한다. 적중 여부와 관계없이 판정 뒤 1,250ms 동안 쉬며, 예고 중 사망하면 예정 타격을 취소한다. 범위 안에서 옆으로 빠져도 피할 수 있어 근접 적과 다른 회피 경로를 검증한다. [KAN-68](https://gametithe.atlassian.net/browse/KAN-68) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190) · [KAN-64](https://gametithe.atlassian.net/browse/KAN-64)

`Assets/ProjectWeapon/Enemies/Ranged/PrototypeRangedEnemySettings.asset`에서 검증 수치를 편집한다. 체력 18과 타격 피해 6은 검증용 가안이다.

## 연결

- 적 오브젝트에 `RangedEnemyActor`를 추가하고 고유 표적 ID와 설정 에셋으로 초기화한다. 적의 이동은 적용하지 않는다. 전투 조립 코드는 런 시각과 현재 플레이어 스냅샷을 매 스텝 `Advance`에 전달한다. 같은 시각의 사망 타격을 먼저 전달하면 예약된 공격이 취소된다.
- 예고는 `Brain.ActiveAttack` 또는 `AttackStarted`의 `Area`와 `HitsAt`을 사용한다. `Area.GetCorner`로 예고의 꼭짓점을 읽고, `AttackResolved`가 반환한 요청을 기존 플레이어 피해 경로에 연결한다. 두 시점 모두 같은 `RangedAttackStrip`을 사용한다. [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)
- Play Mode에서 적 오브젝트를 선택하고 Scene 뷰에 Gizmos를 켜면 생존 상태는 초록 사각형, 피격 직후는 빨강, 사망은 회색으로 보인다. 예고 중에는 주황색 직사각형이 경계를 표시한다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Enemies.Tests.RangedEnemyTests --output "$env:TEMP/KAN-68-editmode.xml"
```

실제 설정과 액터로 접근 거리, 749/750ms 타격 경계, 폭과 길이 양쪽 경계, 방향 고정, 대각선 회피, 1,249/1,250ms 회복, 중복 피격, 예고 중 사망과 정지 상태를 확인한다.
