# KAN-130 사냥 활 기본공격

1등급 `hunting_bow` 원형의 `BasicAttack`을 `HuntingBowSingleArrow.asset`에 연결했다. 기존 시작 무기 카탈로그에서 원형을 조회해 `AutoAttackScheduler`에 전달한다. [KAN-130](https://gametithe.atlassian.net/browse/KAN-130) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

| 에셋 필드 | 검증용 가안 |
| --- | --- |
| 표적 탐색 사거리 | 월드 단위 5 |
| 발동 쿨다운 | 1,100ms |
| 화살 속도 | 초당 월드 단위 10 |
| 화살 수명 | 550ms |
| 적중 피해 요청량 | 8 |

자동공격 콜백은 접근 가능한 가장 가까운 표적을 담은 요청을 받는다. `SingleArrowPattern.TryLaunch`에 요청, 발사마다 새로 발급한 적용 GUID, 공격자 ID, 비활성 `ProjectileLifecycle`을 전달한다. 반환값이 참이면 화살 한 발이 시작됐고, 스케줄러는 이때부터 쿨다운을 적용한다. 거리 5의 표적까지 비행하는 데 500ms가 걸리며, 남은 50ms 동안 화살이 진행할 수 있다. 공격 원점과 표적이 겹치면 해당 위치에서 접촉을 기다린다. [KAN-72](https://gametithe.atlassian.net/browse/KAN-72)

화살의 위치는 발사 후 경과 시간에 따라 갱신된다. 적중 처리기는 실제로 확인한 접촉을 해당 발사의 GUID와 경과 시간으로 전달한다. 첫 유효 표적 접촉에서 피해 요청 한 건을 만들고 종료한다. 차단 지형이나 수명 만료로 종료하면 이후 접촉에서 피해 요청을 반환하지 않는다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.SingleArrowPatternTests --output "$env:TEMP/KAN-130-results.xml"
```

EditMode 테스트는 실제 카탈로그·스케줄러·화살 수명 모델을 연결하고, 플레이어와 표적을 좌표로 배치한 뒤 물리 접촉을 대역에서 전달한다. 1등급 원형 연결, 가장 가까운 유효 표적 조준, 사거리 경계의 적중, 발사 실패 후 같은 프레임 재시도, 쿨다운 경계, 원점 접촉 및 지형 차단을 검증한다.
