# KAN-138 단검 빠른 연속 타격

1등급 단검 `dagger` 원형의 기본공격에 `DaggerFlurry.asset`을 연결했다. 기존 자동공격이 가장 가까운 유효 표적을 선택한다. 한 발동에 같은 표적을 대상으로 두 차례 타격을 예약한다. [KAN-138](https://gametithe.atlassian.net/browse/KAN-138) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

| 에셋 필드 | 테스트용 가안 |
| --- | --- |
| 사거리 | 월드 단위 1.15 |
| 발동 쿨다운 | 650ms |
| 발동당 타격 | 2회 |
| 타격 간격 | 120ms |
| 타격당 피해 요청량 | 5 |

자동공격 실행 콜백에서 `TryCreateFlurry`로 단일 발동을 만들고, 탐험 조정 코드가 매 프레임 `TryResolveNext`에 같은 런의 시각, 현재 공격자 위치와 원래 표적의 최신 좌표를 넘긴다. 첫 타격은 발동 시각, 두 번째는 120ms 후다. 프레임 지연으로 둘 다 처리할 수 있으면 한 번씩 꺼내고 각 피해 요청을 차례대로 적용한다. 결과 시각은 예약한 시각을 유지한다.

타격마다 생존·공격 가능·사거리를 재검사한다. 현재 대상이 빠졌거나 사망했으면 해당 타격만 빗나간다. 다른 대상을 다시 선택하지 않는다. 피해를 적용할 타격은 각각 새 적용 ID를 받아 두 타격이 동일 피해로 중복 제거되지 않는다. 탐험 만료, 귀환 채널링 또는 런 변경이 감지되면 남은 타격을 취소한다. [KAN-77](https://gametithe.atlassian.net/browse/KAN-77) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.DaggerFlurryPatternTests --output "$env:TEMP/KAN-138-editmode.xml"
```

실제 카탈로그와 스케줄러를 대상으로 가장 가까운 표적, 1.15유닛·650ms 경계, 두 타격의 시각과 고유 ID, 표적 무효화와 귀환·만료 시 취소를 검증한다. 테스트는 피해 요청을 수집하는 임시 콜백으로 타격을 재현한다.
