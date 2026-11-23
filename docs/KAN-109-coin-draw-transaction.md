# KAN-109 코인 10회 추첨 거래

`CoinWeaponDrawService.PurchaseTen(requestId, nextExclusive)`은 단일 추첨표를 순서대로 10번 사용한 뒤 코인 차감, 실물 10개와 요청당 영수증 한 건을 KAN-73 스냅샷으로 원자 커밋한다. 기본 가격은 5,000코인 가안이다. 단일 가격을 설정하면 10배를 사용한다. 등급 선택은 기존 추첨표를 따르며 할인, 천장과 최소 등급 보장은 적용하지 않는다. [KAN-109](https://gametithe.atlassian.net/browse/KAN-109) · [KAN-99](KAN-99-coin-draw-transaction.md) · [KAN-69](https://gametithe.atlassian.net/browse/KAN-69)

## 저장과 재시도

- 잔액 4,999에서는 추첨 전에 거절한다. 5,000이면 개인 소유 창고 무기 10개를 만들고 5,000코인을 차감한다. 열 개 중간에 추첨 또는 저장이 실패하면 불완전한 후보가 활성화되지 않는다.
- 스냅샷에 저장된 같은 ID·가격·수량 요청은 원래 순서의 `Receipt.Awards`와 원래 당시에 확정된 잔고를 반환한다. 같은 ID에 다른 수량이나 가격은 `RequestConflict`다.
- 포맷 1의 기존 단일 영수증은 `AwardCount = 1`로 읽는다. 원래 원형·실물·등급 속성은 첫 지급을 가리키며, 배치 영수증에는 나머지 지급을 추가로 저장한다. 도감, 런 정산, 다른 구매 영수증은 새 스냅샷에 함께 보존한다. 저장소를 읽을 때 모든 구매 영수증의 지급 실물 ID를 비교해 중복을 검출한다.
- I/O 실패 뒤에는 같은 ID로 재시도한다. 이미 활성화된 거래의 지급 결과는 다시 추첨하거나 발급하지 않는다. 다른 거래가 먼저 커밋된 경우 기존 리비전 경합 처리 규칙을 따른다. [KAN-73](KAN-73-atomic-snapshot.md)

## 테스트 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.CoinWeaponDraw.Tests --output "$env:TEMP/KAN-109-results.xml"
```

부족·정확한 잔액, 정상 지급 10개, 중간 샘플 실패, 교체 전 실패, 교체 후 응답 유실, 단일·배치 요청 충돌과 기존 단일 영수증 로드를 검증한다.
