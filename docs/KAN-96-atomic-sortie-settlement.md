# KAN-96 탐험 정산 원자 커밋

`RunSettlementCoordinator.TrySettle`에 확정 런 결과, 최초 반입 명세, 종료 시 장착·운반 목록, 같은 탐험의 임시 지갑을 전달한다. 성공·실패 계산을 재사용해 무기와 확정 코인, 도감, 정산 영수증을 한 저장 리비전으로 바꾼다. 성공 시 실제로 보유한 실물만 도감 회수에 반영하고, 실패 시 기존 도감을 보존한다. 탐험 중의 발견 저장은 해당 획득을 소유하는 통합 경계에서 확정한다. [KAN-96](https://gametithe.atlassian.net/browse/KAN-96) · [KAN-87](https://gametithe.atlassian.net/browse/KAN-87) · [KAN-90](https://gametithe.atlassian.net/browse/KAN-90) · [KAN-92](https://gametithe.atlassian.net/browse/KAN-92)

영수증은 `RunSettlement:{RunId}`로 생성한다. 다른 문자열 키를 사용한 구형 영수증도 같은 `RunId`를 재적용하지 않는다. 동시에 다른 결과를 제출하면 먼저 확정된 결과가 유지된다. 기존 결과는 새로 지급하지 않고 반환하며, 관계없는 리비전 경쟁은 호출자가 새 저장본에서 같은 입력을 다시 계산할 수 있게 한다. 저장이 실패하거나 응답을 잃으면 동일 입력으로 재시도한다. 정산 영수증을 받은 통합 주체가 종료된 탐험 세션과 임시 지갑을 폐기하고 확정 결과를 표시한다. 계산과 재호출 과정에서 전달받은 임시 지갑은 변경되지 않는다.

`RunSettlementDetails.Read`에서 버전 1 영수증의 귀환 사유, 확정·소멸 코인, 회수·손실과 대여 실물 ID 목록을 읽을 수 있다. 구형 영수증은 원본을 유지하고 목록 값이 없을 수 있다.
