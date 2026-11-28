# KAN-174 전투 HUD

`CombatHudPanel`은 기존 플레이어 체력, 구역 타이머, 인벤토리와 무기 카탈로그를 읽어 상단에 체력 바·현재/최대 HP·남은 시간·장착 슬롯 두 칸을 표시한다. 실제 선택된 장착 칸에는 왼쪽 표시선을 켠다. 폐성 출격에서는 기존 액션 HUD의 안전 영역에 배치되고, 같은 런·장착 선택을 읽으며 복구 대기나 정산에서 해당 HUD와 함께 숨겨진다. [KAN-174](https://gametithe.atlassian.net/browse/KAN-174) · [KAN-70](https://gametithe.atlassian.net/browse/KAN-70) · [KAN-89](https://gametithe.atlassian.net/browse/KAN-89) · [KAN-176](https://gametithe.atlassian.net/browse/KAN-176)

독립 패널에 만료 상태를 전달하면 `00:00`을 표시한다. 실제 폐성에서는 남은 시간이 양수인 마지막 구간에 `00:01`을 표시하며, 최초 만료 시각에 남은 시간 0으로 결과를 확정하고 HUD를 정산 화면으로 교체한다. 실제 로비에서 두 무기를 장착한 뒤 원거리 적의 피해 6, 타이머, 터치 장착 전환, 만료 경계를 자동 검증한다.

## 연결

1. 기존 HUD Canvas의 안전 영역 `RectTransform`에 `CombatHudPanel`을 한 번 추가한다. 960×540의 참조 해상도와 `Scale With Screen Size` Canvas 구성을 기준으로 상단에 배치된다. 기존 화면처럼 TMP Essential Resources와 한글 글꼴을 준비한다.
2. `Initialize(hangulFont)`를 한 번 호출한다. 매 탐험을 시작할 때 실제 서비스와 프레임 시각을 `Bind(playerHealth, zoneCountdown, inventory, weaponCatalog, sampledAt, activeSlot)`로 전달한다.
3. 기존 탐험 프레임을 처리한 직후 같은 시각으로 `RefreshTime(sampledAt)`를 호출한다. 남은 시간이 양수면 올림해서 초를 표시하며, 만료 시 `00:00`, 활성 탐험이 없으면 `--:--`로 표시한다.
4. 장착 이동이 성공하거나 현재 선택된 장착 슬롯이 바뀔 때 `RefreshEquipment(activeSlot)`을 호출한다. 슬롯 번호는 0부터 시작한다. 선택이 없으면 `null`을 전달한다. 상태 스냅샷은 장착 갱신에서만 읽는다.

HP는 `PlayerHealth.DamageApplied`에서 즉시 갱신한다. 패널을 비활성화하면 구독을 풀고, 다시 활성화하면 현재 HP를 읽는다. 장착 칸이 비었거나 해당 무기 원형을 찾을 수 없을 때는 각각 `비어 있음`과 `미확인 무기`로 상태를 표시한다. HUD의 그래픽은 터치 레이캐스트를 받지 않는다.

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.CombatStatusUI.Tests --output "$env:TEMP/KAN-174-playmode.xml"
```

PlayMode에서 실제 Canvas, 무기 카탈로그, 탐험과 인벤토리를 연결해 체력 감소와 0 HP, 0초 직전과 만료, 장착 교체와 활성 표시, 빈 슬롯 및 원형 누락, 탐험 재연결과 패널 재활성화를 확인한다.

Unity 6000.6.9f1에서 PlayMode 3건을 통과했다. 960×540 게임 뷰에서 체력 감소, 한글 장착명, 활성 슬롯 표시선과 남은 시간이 함께 출력되는 것을 확인했다.
