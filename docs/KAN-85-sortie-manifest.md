# KAN-85 출격 명세와 실물 예약

`SortieStarter.TryStart(zoneId)`는 `RunId.New()`로 출격 ID를 발급한다. 대여를 준비할 때는 ID를 먼저 발급해 `PrepareStarterLoan(runId.Value)`와 `TryStart(runId, zoneId)`에 같은 값을 전달한다. 장착 슬롯 0·1의 개인 실물이나, 개인 반입이 없는 경우 첫 슬롯의 대여 낡은 검을 허용한다. 대여 원형과 같은 런에 발급된 실물 ID를 확인한다. 운반 슬롯은 비워야 한다. [KAN-85](https://gametithe.atlassian.net/browse/KAN-85) · [KAN-45](https://gametithe.atlassian.net/browse/KAN-45) · [KAN-84](https://gametithe.atlassian.net/browse/KAN-84)

`InventoryService.TryStartSortie`는 장착 순서와 출처를 고정한 명세를 만든다. 같은 인벤토리 잠금 아래에서 구역의 런 시작을 확인한 뒤 창고 쪽 기록을 해당 ID의 `RunReserved`로 바꾼다. 이전 탐험의 예약이 남아 있는 동안 새 예약을 거부한다. 실행이 거절되거나 콜백이 예외로 중단되면 장착 기록은 호출 전 상태를 유지한다. 전리품이나 런 이동 시에는 `manifest.Equipped`에서 별도 런 인벤토리를 만들어 사용한다. 마을의 예약 기록과 출격 직전 명세는 유지한다.

호출은 기존 RunState와 같은 게임플레이 스레드에서 처리한다. 시작 콜백은 동기적으로 RunState 시작을 확인한다. 런 종료 뒤에는 해당 ID의 예약을 성공·실패 정산에서 처리하며, 잠긴 예약 상태의 저장 및 복구는 정산과 복구 흐름의 책임으로 둔다. [KAN-87](https://gametithe.atlassian.net/browse/KAN-87) · [KAN-90](https://gametithe.atlassian.net/browse/KAN-90) · [KAN-189](https://gametithe.atlassian.net/browse/KAN-189)

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Dungeon.Tests.SortieStarterTests --output "$env:TEMP/KAN-85-editmode.xml"
```
