# KAN-198 폐성 출격 유랑 검사 외형

로비에서 실제 1구역으로 출격하면 시작 플레이어의 외형에 KAN-28의 유랑 검사 4방향 대기·이동·회피 프레임을 표시한다. 32 PPU와 Point 샘플링을 사용하는 기존 스프라이트를 재사용하고, 발 피벗은 플레이어 중심보다 0.25 월드 단위 아래에 둔다. [KAN-198](https://gametithe.atlassian.net/browse/KAN-198) · [KAN-28](https://gametithe.atlassian.net/browse/KAN-28)

1구역 씬에서만 공유 플레이어 프리팹의 점 렌더러를 비활성화한다. 자식 뷰는 이동의 실제 입력과 활성 회피 방향을 매 프레임 읽고, 조작이 잠기면 마지막 방향으로 대기한다. 물리와 전투 입력은 기존 경로를 유지한다.

## 재현

1. Unity에서 `Assets/Scenes/SortieLoop/SortieLoop.unity`를 열고 Play를 누른다.
2. 출격 장비 화면에서 확정한다. 1구역 시작 위치에 남쪽을 보는 유랑 검사가 보인다.
3. WASD 또는 터치 조이스틱을 눌러 방향 이동을 확인한다. 입력을 놓으면 마지막 방향에서 대기한다.
4. 회피 버튼 또는 스페이스를 눌러 실제 회피 방향의 프레임을 확인한다.

```powershell
unity test . --mode PlayMode --filter 'ActualSortieDisplaysWandererForIdleMovementAndDodge|ActualCombatHudCastsTheStarterLoanAndReturnsThroughThePortal|OldSwordBasicAndManualSlashDefeatPlacedBossOnce' --output "$env:TEMP/KAN-198-playmode.xml" --timeout 900
```
