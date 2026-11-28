# KAN-36 귀환·시간초과·사망 정산 교차 회귀

Unity EditMode의 `ExtractionSettlementRaceTests`는 실제 1구역 구역 설정, 출격 예약, 귀환 채널과 피해 이벤트를 사용해 같은 프레임의 완료 후보와 만료를 함께 처리한다. 만료 1ms 전은 성공 가능하고, 정확히 만료 시각이면 시간초과다. 유효 피해와 귀환 완료가 같은 시각에 오면 사망 또는 취소가 앞선다. 입력 크기 0.19/0.20 경계에서도 기존 이동 취소 규칙을 검증한다. [KAN-36](https://gametithe.atlassian.net/browse/KAN-36) · [KAN-81](https://gametithe.atlassian.net/browse/KAN-81) · [KAN-89](https://gametithe.atlassian.net/browse/KAN-89)

같은 실행에서 플레이어, 가방과 기존 창고를 실제로 생성하고, 디스크의 원자 저장을 다시 열어 같은 결과를 10번 재요청한다. 영수증은 한 개다. 성공에서 실제 보유 장비와 전리품, 임시 코인의 지급을 확인한다. 실패에서 반입·전리품 실물과 임시 코인의 손실, 기존 확정 코인·창고·도감 보존을 확인한다. [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)
