# KAN-29 적 공격 예고와 피격 픽셀 샘플

`Assets/ProjectWeapon/Art/EnemyCues/EnemyCue.prefab`은 적 예고의 얇은 붉은 외곽선과 실제 적중의 작은 황백색 픽셀 스파크를 제공한다. 외곽선은 2px(32 PPU) 두께와 반투명도를 사용하고, 내부를 비운다. 16×16px 피격 스파크는 월드에서 0.5유닛이며 0.12초 보인다. 생성 원본은 `art-source/Export-EnemyImpactSpark.ps1`이다. [KAN-29](https://gametithe.atlassian.net/browse/KAN-29) · [KAN-21](https://gametithe.atlassian.net/browse/KAN-21)

- 근접 적의 `AttackStarted`에서 잠긴 `AreaDamageRequest`를 `ShowSector`로 전달한다. 원점·방향·반경·양쪽 각도는 실제 판정과 일치한다.
- 원거리 적의 `AttackStarted`에서 `RangedAttackStrip.GetCorner(0~3)`의 네 좌표를 순서대로 `ShowStrip`에 전달한다. 기본 정렬 레이어에서 타일 바닥 0, 예고 외곽선 1, 캐릭터와 무기 2~3, 스파크 4 순서를 사용한다. [KAN-64](https://gametithe.atlassian.net/browse/KAN-64) · [KAN-68](https://gametithe.atlassian.net/browse/KAN-68)
- 공격이 판정되거나 취소되면 `ClearTelegraph`를 호출한다. 승인된 피해 이벤트에서 `FlashHit`에 해당 적의 위치를 전달한다. 프리팹의 스파크를 캐릭터 얼굴이나 HUD와 겹치지 않도록 오프셋으로 배치한다.

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.Art.EnemyCues.Tests --output "$env:TEMP/KAN-29-playmode.xml"
```
