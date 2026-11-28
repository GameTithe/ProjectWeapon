# KAN-135 철퇴 기본공격

1등급 철퇴 `mace`의 `BasicAttack`에 `MaceCircularStrike.asset`을 연결했다. 기존 시작 무기 카탈로그에서 철퇴를 조회해 `AutoAttackScheduler`에 넘긴다. [KAN-135](https://gametithe.atlassian.net/browse/KAN-135) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-191](https://gametithe.atlassian.net/browse/KAN-191)

| 에셋 필드 | 테스트용 가안 |
| --- | --- |
| 반경 및 표적 선택 사거리 | 월드 단위 1.5 |
| 쿨다운 | 1,200ms |
| 표적당 요청 피해량 | 12 |

스케줄러가 사거리 이내의 가장 가까운 유효 표적을 찾으면 공격을 한 번 시작한다. `CircularStrikePattern.TryCreateStrike`는 공격자 ID, 적용 GUID, 현재 프레임의 후보 목록을 받아 공격자를 중심으로 피해 배치를 만든다. 표적 방향이나 원점과의 겹침에 관계없이 경계를 포함한 원 안의 유효 표적마다 피해 요청을 한 건 생성한다. 적용 시각은 스케줄러가 전달한 발동 시각이다. [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

아래 EditMode 테스트는 피해 배치를 기록하는 대역 콜백을 연결해 실물 에셋의 동작을 재현한다. 타격을 수락한 콜백은 참을 반환한다. 실행 거부 시 쿨다운은 발생하지 않는다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.CircularStrikePatternTests --output "$env:TEMP/KAN-135-results.xml"
```

| 검증 조건 | 기대 결과 |
| --- | --- |
| 카탈로그에서 철퇴 조회 | 1등급 철퇴에 원형 타격 패턴이 한 개 연결돼 있다. |
| 옆·뒤·경계에 적을 두고 일부 표적 ID를 중복 전달 | 공격자를 중심으로 유효 표적당 즉시 피해 요청 한 건을 만든다. |
| 선택 표적이 공격자와 겹침 | 반경이 유지된다. |
| 발동 후 0ms, 1,199ms, 1,200ms에 재요청 | 1,200ms 경계에서 새 적용 ID로 한 번 발동한다. |
| 유효 표적 부재, 잘못된 적용 ID, 귀환 진행 | 피해 배치를 생성하지 않는다. |
