# KAN-71 무기 실물의 원자적 위치 이동

`InventoryService`는 실물 ID 하나에 현재 위치 하나를 보관한다. 초기 목록의 실물·ID 유효성은 KAN-63 검사기를 사용하고, 여러 실물이 동일한 장착 또는 운반 슬롯을 차지하면 목록을 거절한다. 창고는 여러 실물이 같은 위치 종류를 사용하는 보관 공간이다. [KAN-71](https://gametithe.atlassian.net/browse/KAN-71) · [KAN-63](KAN-63-weapon-instance.md)

## 호출 규칙

- `Move(instanceId, expectedSource, destination)`에 현재로 알려진 원본 위치와 목적지를 넘긴다. 이동 대상은 창고, 장착 슬롯, 운반 슬롯이다. 장착과 운반은 같은 번호라도 서로 다른 슬롯이다.
- 현재 위치가 원본과 맞고 목적지 슬롯이 비어 있으면 `Moved`를 반환한다. 기존 실물 ID, 원형 ID와 출처를 유지한 상태에서 위치만 교체한다.
- 원본이 현재와 다르면 `SourceMismatch`를 반환한다. 같은 이동을 다시 보냈을 때 첫 이동이 이미 원래 위치를 떠났다면 이 결과를 받는다. 원본과 목적지가 현재와 모두 같으면 `AlreadyAtDestination`이다. 호출자는 `Moved`일 때만 새 변경으로 취급한다.
- 목적지에 다른 실물이 있으면 `DestinationOccupied`다. 다른 실물 밀어내기나 교환은 수행하지 않는다. 존재하지 않는 실물은 `MissingInstance`, 미지정 위치·유효하지 않은 ID·탐험 예약을 대상으로 한 요청은 `InvalidRequest`다. 기존 출처 계약에 따라 대여 실물을 창고로 옮기는 요청은 `ProvenanceRestricted`다.
- 검사와 위치 교체는 하나의 잠금 안에서 처리한다. `Snapshot()`은 같은 잠금 안에서 순간의 실물 목록을 복사해 반환한다. 저장과 확정 변경 이벤트는 상위 조정 계층의 계약을 따른다. 실제 슬롯 수, 출격 예약, 판매, 정산은 각 후속 작업의 범위다. [KAN-19](KAN-19-domain-contracts.md) · [KAN-80](https://gametithe.atlassian.net/browse/KAN-80) · [KAN-85](https://gametithe.atlassian.net/browse/KAN-85)

## 실행과 재현

Unity 프로젝트 루트의 PowerShell에서 실행한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Inventory.Tests --output "$env:TEMP/KAN-71-editmode.xml" --timeout 900
```

| 사례 | 기대 결과 |
| --- | --- |
| 창고 → 장착 → 운반 → 창고 | 실물 ID·원형·출처가 동일하고 어느 순간에도 한 위치만 보유 |
| 목적지 점유 후 재시도 | 실패 시 두 실물은 이전 상태 유지, 슬롯 비우고 나면 성공 |
| 성공 직후 같은 원본·목적지 재전달 | 원본 불일치로 추가 변경 없음 |
| 초기 목록에 동일 실물 ID 또는 동일 장착 슬롯 | 초기 목록 거절 |
| 동일 실물에 장착·운반 이동을 동시에 요청 | 한 요청만 성공, 남은 위치 한 개 |
| 다른 두 실물로 같은 슬롯을 동시에 요청 | 한 요청만 성공, 나머지는 원래 위치 유지 |
| 대여 실물의 창고 이동 또는 예약 상태 이동 | 해당 위치 요청 거절, 기존 실물 유지 |


## 이번 작업의 검증 결과

2026-11-23 Unity 6000.6.9f1 및 Unity CLI 1.0.1에서 Inventory EditMode 테스트 21건을 실행했고 모두 통과했다. KAN-71이 추가한 7건에는 같은 실물과 대상 슬롯을 동시에 요청하는 두 테스트가 포함된다. XML과 실행 로그의 로컬 위치는 `%TEMP%/ProjectWeapon-KAN71-1795430318`다.
