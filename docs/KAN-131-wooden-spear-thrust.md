# KAN-131 나무 창 직선 찌르기

`wooden_spear` 1등급 원형에 `WoodenSpearThrust.asset`을 연결한다. 가안은 사거리 2유닛, 950ms 쿨다운, 피해 10이다. [KAN-131](https://gametithe.atlassian.net/browse/KAN-131) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

자동공격 실행 콜백에서 `SpearThrustPattern.TryCreateThrust`에 요청을 전달한다. 피해 수신기가 서로 다른 발동을 구분할 수 있도록 매 발동에 새 타격 ID를 발급한다. 유효하면 발동 시점에 선택된 표적의 피해 요청 한 건을 처리한다. 같은 선상에 있는 뒤쪽 대상은 관통하지 않는다. 검증 중 Scene 뷰에 `SpearThrust.Origin`에서 `SpearThrust.Tip`까지 선을 그려 방향을 확인할 수 있다.

아래 EditMode 검증은 실제 원형과 스케줄러로 가장 가까운 유효 표적, 사거리·쿨다운 경계, 동일 시점의 중복 발동, 즉시 피해 한 건을 재현한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.SpearThrustPatternTests --output "$env:TEMP/KAN-131-editmode.xml"
```
