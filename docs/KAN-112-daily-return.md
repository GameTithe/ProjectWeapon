# KAN-112 첫 일일 미션 진행 저장

[KAN-112](https://gametithe.atlassian.net/browse/KAN-112) · [KAN-57](https://gametithe.atlassian.net/browse/KAN-57) · [KAN-115](https://gametithe.atlassian.net/browse/KAN-115)

온라인 정산 호스트는 검증된 계정 ID, 해당 계정의 확정 저장본, 런 ID와 확정 결과의 서버 시각을 `DailyReturnMission.Record`에 전달한다. 정상적인 `RunSettlement:{RunId}` 영수증으로 성공을 확인하고 UTC 날짜와 미션 ID `daily_safe_return`를 기준으로 진척을 확정한다. 클라이언트 시간, 접수 시각과 기기 계정 정보는 이 판단에 사용하지 않는다. 재접속 화면은 권위 서버의 현재 UTC 날짜를 `Read`에 전달한다. 정산 영수증에는 계정 ID나 서버의 UTC 시각이 들어 있지 않으므로, 호스트에서 같은 검증 계정의 저장본과 런별 최초 유효 시각을 조회한다. 정산 후 진행 저장에 실패하면 동일한 계정·런·유효 시각으로 재시도한다.

일부 날에 여러 차례 성공해도 한 런만 그 날의 완료에 반영된다. 다른 성공 런의 중복 자료도 기록해 날짜를 바꾸어 다시 사용하지 못하도록 한다. 상태는 성공한 런의 관측 및 완료만 저장한다. 확정 코인은 KAN-115에서 지급한다.

`LocalDailyMissionProgressStore`는 Editor에서 계정과 날짜 동시성·재접속·재시작을 검증하는 독립 저장 구현이다. 보호된 온라인 저장에서는 같은 `IDailyMissionProgressStore` 원자 계약을 사용한다. 플레이어 앱에는 Editor 저장 구현이 포함되지 않는다.

EditMode `DailyReturnMissionTests`에서 실제 출격·성공·실패·원자 정산, UTC 날짜 직전과 직후 역순 도착, 재접속 10회, 계정 분리, 유효하지 않은 영수증과 동시 저장을 검증한다.
