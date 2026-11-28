# KAN-111 도감 회수 단계 코인

[KAN-111](https://gametithe.atlassian.net/browse/KAN-111) · [KAN-103](https://gametithe.atlassian.net/browse/KAN-103) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

성공 정산에서 회수·숙련 원형의 확정 종수를 센다. 첫 5종에서 500코인, 10종에서 1,000코인, 15종에서 2,000코인을 지급한다. 같은 원형의 실물 여러 개, 발견 실루엣, 구형의 알 수 없는 상태는 추가 종수를 만들지 않는다. 실패 시 회수 코인과 기존 단계 영수증을 그대로 유지한다.

성공 정산의 같은 원자 저장에 확정 코인, 새 도감, 런 영수증과 단계별 `CatalogMilestone:{종수}` 영수증을 쓴다. 단계 영수증의 출처는 지급을 확정한 런 ID다. 런 영수증의 기존 `AwardedCoins` 값은 그 탐험 자체에서 얻은 코인의 양이다. 단계 지급 내역은 저장본의 `CatalogMilestones`에서 읽는다.

이전 저장 파일은 새 필드가 없어도 읽힌다. 이미 해당 종수를 채웠고 영수증이 없는 보상은 다음 유효한 성공 정산에서 한 번 처리한다. 현행 저장 경로는 `SaveSnapshot.Next`로 수정하지 않는 영수증을 계속 전달한다.

EditMode `CatalogMilestoneRewardTests`에서 4/5·9/10·14/15 경계, 동일 원형 중복과 기존 숙련, 구형 쓰기 형태, 사망과 시간초과, 지급 전 저장 장애, 지급 뒤 응답 유실, 동일 런 재호출 10회 및 코인 뽑기와 중복 무기 판매 이후에도 남는 양쪽 영수증을 실제 파일에서 검증한다.
