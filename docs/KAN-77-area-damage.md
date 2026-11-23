# KAN-77 공통 범위 피해 판정

`AreaDamageRequest.Circle` 또는 `Sector`에 공격 적용 GUID, 공격자 ID, XY 원점, 월드 단위 반경, 요청 피해량을 넣는다. 부채꼴은 방향과 전체 각도(도)를 추가로 받는다. `AreaDamageSelector.FindTargets`는 기존 `TargetCandidate` 목록에서 조건에 맞는 표적을 찾아 `DamageRequest`로 반환한다. [KAN-19](https://gametithe.atlassian.net/browse/KAN-19) · [KAN-52](https://gametithe.atlassian.net/browse/KAN-52) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

- 원과 부채꼴 모두 반경과 경계를 포함한다. 부채꼴은 중심 방향 좌우로 전체 각도의 절반을 적용한다. 길이가 다른 같은 방향 벡터는 같은 결과를 내며 원점의 표적은 각도 제한에 걸리지 않는다.
- 좌표·반경·피해량은 유한해야 하며 반경·피해량은 0 이상이다. 부채꼴 방향은 유한한 0이 아닌 벡터이고 전체 각도는 0~360도다. 유효하지 않은 요청 또는 빈 후보 목록은 피해 요청을 만들지 않는다. 목록 자체가 `null`이면 호출 오류다.
- 공격자, 사망하거나 공격 불가능한 표적, 좌표와 ID가 유효하지 않은 후보는 제외한다. 한 번의 판정에 해당 적용의 전체 후보 스냅샷을 전달한다. 콜라이더가 같은 실물 ID를 공유하면 이번 공격 적용의 결과에 한 번만 포함한다.
- 반환값은 적용 ID·공격자 ID·표적 ID·요청 피해량이다. 같은 적용의 결과를 다시 전달하는 소비자는 적용 ID와 표적 ID의 조합으로 중복 처리를 막는다. 별도 타격에는 새 적용 ID를 전달한다. 요청 피해량은 실수 그대로 반환한다. 정수 HP로 변환하는 규칙, HP 변경, 피격 효과와 스킬별 수치는 후속 정책이나 작업에서 다룬다. [KAN-70](https://gametithe.atlassian.net/browse/KAN-70) · [KAN-129](https://gametithe.atlassian.net/browse/KAN-129)

## 재현 절차

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Combat.Tests.AreaDamageSelectorTests --output "$env:TEMP/KAN-77-results.xml"
```

| 조건 | 기대 결과 |
| --- | --- |
| 반경 5에서 원점으로부터 상대 위치가 `(3, 4)`인 표적 | 반경 경계에 있으므로 포함한다. `(3, 4.01)`은 제외한다. |
| 양의 X 방향과 전체 90도 부채꼴에서 `(1, 1)`, `(1, -1)` | 양쪽 45도 경계에 있으므로 포함한다. `(1, 1.001)`은 제외한다. |
| 동일한 실물 ID의 콜라이더가 3개임 | 해당 공격 적용에는 피해 요청 한 건을 반환한다. |
| 공격자 또는 사망·무효 후보, 비어 있는 적용 ID, 음수 반경, 길이 0인 부채꼴 방향 | 피해 요청을 만들지 않는다. |
