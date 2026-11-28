# KAN-138 단검 빠른 연속 타격

1등급 단검 `dagger` 원형의 기본공격에 `DaggerFlurry.asset`을 연결했다. 기존 자동공격이 가장 가까운 유효 표적을 선택한다. 한 발동에 같은 표적을 대상으로 두 차례 타격을 예약한다. [KAN-138](https://gametithe.atlassian.net/browse/KAN-138) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

| 에셋 필드 | 테스트용 가안 |
| --- | --- |
| 사거리 | 월드 단위 1.15 |
| 발동 쿨다운 | 650ms |
| 발동당 타격 | 2회 |
| 타격 간격 | 120ms |
| 타격당 피해 요청량 | 5 |

실제 1구역의 `ZoneOneEncounter`가 자동공격 콜백에서 연타를 시작한다. 첫 타격은 발동 즉시 적용하고, 두 번째는 120ms 뒤의 첫 프레임에 처리한다. 매 타격의 `ScheduledAt`은 예약 시각을 보존한다. 지연된 프레임에서 적의 현재 위치와 생존을 검사한 피해의 `EffectiveAt`은 그 프레임의 시각이다. 예정된 타격을 한 번씩 처리할 때마다 적에게 즉시 적용하고 다음 타격을 위해 표적을 다시 확인한다.

타격마다 생존·공격 가능·사거리를 재검사한다. 현재 대상이 빠졌거나 사망했으면 해당 타격만 빗나간다. 다른 대상을 다시 선택하지 않는다. 피해를 적용할 타격은 각각 새 적용 ID를 받아 두 타격이 동일 피해로 중복 제거되지 않는다. 탐험 만료, 귀환 채널링 또는 런 변경이 감지되면 남은 타격을 취소한다. [KAN-77](https://gametithe.atlassian.net/browse/KAN-77) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.DaggerFlurryPatternTests --output "$env:TEMP/KAN-138-editmode.xml"
unity test . --mode PlayMode --filter ProjectWeapon.ZoneOneGreybox.Tests.DaggerFlurryEncounterTests --output "$env:TEMP/KAN-138-playmode.xml"
```

실제 카탈로그와 스케줄러를 대상으로 가장 가까운 표적, 1.15유닛·650ms 경계, 두 타격의 시각과 고유 ID, 표적 무효화와 귀환·만료 시 취소를 검증한다. PlayMode는 1구역 씬에 단검을 장착한 상태로 들어가 적 체력을 확인한다. 100ms와 270ms 프레임에서 각각 5 피해를 받고, 220ms에 예정된 타격을 처리할 때 같은 프레임을 다시 읽어도 중복 피해가 발생하지 않는다. 귀환을 시작했다가 취소한 경우와 두 번째 타격 예정 시각이 탐험 마감과 같은 경우에도 잔여 피해가 없음을 확인한다.
