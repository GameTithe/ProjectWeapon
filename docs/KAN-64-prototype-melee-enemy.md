# KAN-64 폐성 근접 적 행동

적은 플레이어 중심점까지 1.25유닛 밖이면 직접 추적한다. 거리가 1.25유닛 이내가 되면 이동을 멈추고, 450ms 예고가 시작할 때 적의 위치와 방향을 고정한다. 타격은 같은 위치·방향과 1.4유닛 반경, 90°의 부채꼴 판정을 한 번 사용한다. 이후 타격 시각부터 850ms 동안 재행동을 기다린다. 예고 중 사망하면 예고와 타격을 취소한다. [KAN-64](https://gametithe.atlassian.net/browse/KAN-64) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

`Assets/ProjectWeapon/Enemies/Melee/PrototypeMeleeEnemySettings.asset`에서 검증판 수치를 편집한다. 이동 속도 2.2유닛/초, 체력 24, 타격 피해 8은 검증용 가안이다.

## 연결

- 적 GameObject에 `MeleeEnemyActor`와 중력 0의 동적 `Rigidbody2D`, 필요한 충돌체를 붙인다. 해당 런의 고유 표적 ID와 설정 에셋을 `Initialize`에 넘긴다. 플레이어 전용 스냅샷에 적 또는 다른 아군을 넣지 않는다.
- 전투 조립 지점은 고정 스텝마다 `Advance(runTime, playerSnapshot)`을 호출한다. 예정된 타격과 실제 입력을 유효 시각 순으로 전달한다. 같은 시각의 플레이어 타격은 `ApplyDamage`로 먼저 적용한다. 표적 좌표는 Physics 2D 바디 중심에서 읽는다.
- 예고는 `Brain.ActiveAttack` 또는 `AttackStarted`의 `Area`와 `HitsAt`을 읽는다. `AttackResolved`는 잠긴 예고와 그 면적 안에서 확인된 피해 요청 목록을 준다. 피해 요청은 기존 플레이어 피해 경로에 전달한다. `DamageApplied`, `Died`로 적 피격·사망 연출을 연동한다.
- 임시 표시는 Play Mode에서 해당 적 오브젝트를 선택하고 Scene 뷰의 Gizmos를 켠다. 초록 원은 생존, 빨강 원은 피격 직후, 회색 원은 사망을 나타낸다. 예고 중에는 주황색 부채꼴 외곽을 확인할 수 있다. 플레이어를 부채꼴 밖으로 옮기거나 예고 도중 적을 처치해 빗나감과 취소를 확인한다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Enemies.Tests.MeleeEnemyTests --output "$env:TEMP/KAN-64-editmode.xml"
unity test . --mode PlayMode --filter ProjectWeapon.Enemies.Tests.MeleeEnemyActorTests --output "$env:TEMP/KAN-64-playmode.xml"
```

EditMode는 접근 거리, 각도·반경 경계, 449ms와 450ms의 타격 경계, 849ms와 850ms의 후딜 경계, 발동 횟수, 선택 좌표 고정, 사망 취소, 피격 중복 방지를 검증한다. PlayMode는 실제 2D 리지드바디의 이동 및 예고·사망에서 정지를 확인한다.
