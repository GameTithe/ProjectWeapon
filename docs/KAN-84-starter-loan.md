# KAN-84 무료 낡은 검 대여

출격 준비에서 소유 무기를 반입하지 않을 때 `InventoryService.PrepareStarterLoan(runId)`가 `old_sword` 실물 하나를 장착 0번 슬롯에 발급한다. 기존 낡은 검 원형을 쓰고 실물에는 `Loan` 출처를 기록한다. 장착된 소유 실물 또는 같은 탐험에 예약된 소유 실물이 있으면 발급을 생략한다. 미반입 창고 보유품은 그대로 남고 운반 슬롯은 비워 둔다. [KAN-84](https://gametithe.atlassian.net/browse/KAN-84) · [KAN-45](https://gametithe.atlassian.net/browse/KAN-45)

| 준비 상태 | 반환 및 결과 |
| --- | --- |
| 소유 무기 미반입, 장착 가능 | `Issued`. 첫 슬롯에 대여 실물 한 개 추가 |
| 동일한 탐험으로 재시도 | `AlreadyIssued`. 기존 ID와 현재 위치를 반환하며, 이미 인벤토리를 떠난 대여품은 추가 발급하지 않음 |
| 소유 무기 장착 또는 해당 탐험에 예약 | `OwnedWeaponEquipped`. 인벤토리 변경 없음 |
| 운반품 또는 다른 대여품이 이미 장비에 존재 | `InventoryNotPrepared`. 인벤토리 변경 없음 |
| 잘못된 탐험 ID, 장착 칸 없음, 실물 ID 충돌 | 오류 상태 반환. 인벤토리 변경 없음 |

발급과 재요청 검사는 기존 인벤토리 잠금 안에서 처리한다. 탐험 ID로부터 안정적인 실물 ID를 도출하므로 기존 대여 레코드가 복원되면 동일한 요청을 판별한다. 현재 서비스는 대여품이 바닥으로 나간 뒤의 발급 기록도 유지한다. 호출 지점은 출격을 확정하는 준비 단계이며, 진행 중 탐험의 재개 정책과 출격 상태 롤백은 각 후속 계약에서 다룬다. [KAN-47](https://gametithe.atlassian.net/browse/KAN-47) · [KAN-85](https://gametithe.atlassian.net/browse/KAN-85)

`InventoryService.CheckSaleProvenance`는 대여 ID를 `ProvenanceRestricted`로 거부한다. 소유품은 `RequiresSaleRules`로 기존 판매 정책을 계속 검증한다. 창고 이동은 `Move`가 같은 대여품 제약으로 거절한다. [KAN-51](https://gametithe.atlassian.net/browse/KAN-51) · [KAN-71](https://gametithe.atlassian.net/browse/KAN-71)

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Inventory.Tests.StarterLoanTests --output "$env:TEMP/KAN-84-editmode.xml"
```

시작 무기 카탈로그의 실제 낡은 검, 동시·반복 요청, 이동과 인벤토리 복원 뒤 재시도, 이미 빠진 실물, 판매·창고 이동 제한, 준비 실패 시의 기존 재고를 검증한다.
