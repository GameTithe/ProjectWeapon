# KAN-90 사망·시간초과 손실 계산

`FailedSortieSettlement.Calculate`는 탐험에서 확정한 실패 신호, 출격 반입 명세, 최신 저장본, 종료 시의 장착·운반 목록, 지갑의 탐험 코인 제안을 읽는다. 최초 반입 실물은 필드에 떨어뜨렸어도 손실에 포함한다. 탐험에서 획득해 종료 시 남은 전리품과 임시 코인은 소멸한다. 대여품 소멸은 별도 목록에 기록한다. [KAN-90](https://gametithe.atlassian.net/browse/KAN-90) · [KAN-45](https://gametithe.atlassian.net/browse/KAN-45) · [KAN-85](https://gametithe.atlassian.net/browse/KAN-85)

반입 실물과 같은 원형이어도 ID가 다른 창고 무기를 보존한다. 기존 확정 코인과 도감도 유지한다. 기존 영수증과 정산 이력은 호출자가 최신 저장본에서 그대로 보존한다. 같은 입력으로 계산을 반복할 때 원본 저장값과 런 가방은 그대로다. 다른 탐험·저장 잔액 또는 충돌하는 실물의 입력은 거부하고 중간 결과를 반환하지 않는다. [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)
