# KAN-142 연사궁 기본공격

2등급 `repeater_bow`는 `PrototypeRepeaterBowWeaponCatalog`로 조회한다. 기본공격은 `RepeaterBowVolley.asset` 하나를 사용한다. 검증값은 표적 사거리 5유닛, 발동 간격 1000ms, 화살 3발, 발사 간격 120ms, 초당 속도 10유닛, 수명 600ms, 적중당 피해 6이다. 값은 패턴 에셋에서 조정한다. [KAN-142](https://gametithe.atlassian.net/browse/KAN-142) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

스케줄러가 가장 가까운 유효 표적을 선택하면 콜백에서 `TryCreateVolley`로 발동을 만들고, 비활성 `ProjectileLifecycle`에 `TryLaunchNext`를 호출해 첫 발을 발사한다. 나머지 발은 같은 원점과 방향에서 각각 120ms, 240ms 후 발사할 수 있다. 원점에 겹친 표적은 그 위치에서 접촉을 처리한다. 조정 코드는 실제 발사 시각을 각 화살의 경과 시간 원점으로 기록한다. 늦은 프레임에 예정된 발사가 여러 개면 각 슬롯을 한 번씩 처리한다. 예약 중 런이 끝나거나 귀환 채널링이 시작되면 남은 발을 취소한다.

발사에 피해가 붙지 않는다. 확인된 첫 표적 접촉에만 KAN-72가 해당 화살의 적용 ID로 피해를 요청한다. 지형에 막히거나 수명이 끝난 화살은 종료한다. 에셋 검증에서 실제 무기 카탈로그·스케줄러·투사체 수명에 좌표와 접촉 대역을 전달한다. [KAN-72](https://gametithe.atlassian.net/browse/KAN-72)

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.RepeaterBowPatternTests --output "$env:TEMP/KAN-142-editmode.xml"
```
