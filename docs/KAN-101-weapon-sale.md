# KAN-101 중복 무기 판매

`WeaponSaleService.TrySell(requestId, instanceId)`로 마을 창고의 실물 한 개를 판매한다. 요청 ID는 판매 확정마다 새로 만들고 응답 유실이나 저장 충돌의 재시도에서 같은 ID를 보낸다. 확정 코인과 실제 보유 실물, 판매 영수증은 하나의 저장 리비전에서 반영한다. [KAN-101](https://gametithe.atlassian.net/browse/KAN-101) · [KAN-51](https://gametithe.atlassian.net/browse/KAN-51) · [KAN-73](https://gametithe.atlassian.net/browse/KAN-73)

판매 가능한 개인 실물은 창고에 있어야 하며 동일 원형의 다른 소유 실물이 창고 또는 마을 장착에 남아 있어야 한다. 대여품은 판매하거나 중복으로 셀 수 없다. 출격 또는 정산 중에는 판매를 막고, 저장된 출격 예약이 남아 있는 상태에서도 새 거래를 시작하지 않는다. 판매가는 원형의 등급을 기준으로 `PrototypeWeaponSalePrices.asset`에서 읽으며 초깃값은 1~5등급 각각 30, 75, 150, 300, 450코인이다.

확정 영수증은 요청 ID, 제거한 실물 ID, 원형, 등급, 가격과 판매 직후 잔고를 보관한다. 같은 요청과 같은 실물의 재시도는 기존 영수증 및 현재 저장본을 반환한다. 요청 ID가 다른 실물에 재사용되면 `RequestConflict`다. 먼저 다른 거래가 저장되면 `RevisionConflict`로 최신 저장본을 돌려주고, 같은 키를 다시 보내면 최신 재고로 중복 여부를 한 번 더 검증한다. 저장 I/O 오류가 나면 동일 요청으로 다시 호출해 이미 기록된 영수증을 조회할 수 있다.

판매와 뽑기 경로는 도감과 기존 영수증 컬렉션을 다음 리비전으로 전달한다. 포맷 1에서 별도 판매 기록이 없는 저장본은 빈 판매 목록으로 열린다. 컴포지션은 같은 런과 프로필 저장소를 사용하고, 화면의 확인 문구와 버튼 활성화는 해당 UI에서 연결한다. [KAN-99](https://gametithe.atlassian.net/browse/KAN-99) · [KAN-175](https://gametithe.atlassian.net/browse/KAN-175)

```powershell
unity test . --mode EditMode --filter ProjectWeapon.WeaponSale.Tests --output "$env:TEMP/KAN-101-editmode.xml"
```

후속 코인 뽑기와 탐험 정산은 판매 영수증을 새 저장본에 함께 전달한다. 판매 후 성공 귀환을 저장하고 재시도해도 이전 영수증과 판매된 실물의 부재를 유지한다.
