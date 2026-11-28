# KAN-35 창고·장착·출격 소유권 통합 회귀

[KAN-35](https://gametithe.atlassian.net/browse/KAN-35) · [KAN-45](https://gametithe.atlassian.net/browse/KAN-45) · [KAN-62](https://gametithe.atlassian.net/browse/KAN-62) · [KAN-85](https://gametithe.atlassian.net/browse/KAN-85) · [KAN-90](https://gametithe.atlassian.net/browse/KAN-90)

EditMode `SortieOwnershipFlowTests`는 실제 무기 원형과 창고를 사용한다. 두 장착품을 반복 이동하고 실전 장착 선택을 바꾼 뒤 같은 탐험의 예약과 현장 실물을 검사한다. 창고에는 같은 원형의 미반입 실물을 별도 ID로 둬 원형 이름이나 등급으로 상실 품목을 잘못 제거하면 테스트가 실패한다. 각 시나리오는 사망 또는 시간초과로 두 차례 정산과 저장 재열기를 반복한다.

테스트는 동시에 창고와 출격 인벤토리에 나타나는 ID, 잘못된 예약 이동 및 중복 출격, 반복 정산의 재지급과 미반입 무기·기존 코인·도감의 변경을 검출한다.
