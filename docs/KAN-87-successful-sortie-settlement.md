# KAN-87 성공 귀환 정산 계산

`SuccessfulSortieSettlement.Calculate`에 확정 귀환 결과, 최초 반입 명세, 종료 시 실제 장착·운반 실물, 지갑의 탐험 코인 제안을 입력한다. 보유 소유 장비와 획득품은 원래 실물 ID·원형·출처로 창고에 돌아간다. 버렸던 반입품은 같은 ID를 다시 주운 뒤 귀환했을 때 회수한다. 대여품은 반납 목록으로 보내며, 현장에 두고 온 반입품과 대여품은 도감 회수 목록에서 제외한다. 도감 회수 대상은 실제 회수 실물의 고유 원형 목록으로 제공한다. [KAN-87](https://gametithe.atlassian.net/browse/KAN-87) · [KAN-45](https://gametithe.atlassian.net/browse/KAN-45) · [KAN-49](https://gametithe.atlassian.net/browse/KAN-49)

기존 지갑의 유효한 탐험 코인 제안이 지급 금액을 결정한다. 한 입력을 반복해도 원본 가방과 지갑은 바뀌지 않는다. 원본 창고와의 ID 충돌 및 잔액·리비전 확인, 도감 기록 적용, 저장 확정은 KAN-96 정산 호출자가 담당한다. [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)
