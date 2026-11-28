# KAN-171 메인 로비 내비게이션

로비 캔버스에서 홈과 장착무기·창고·출격·도감·뽑기 화면을 각각 같은 부모 아래 둔다. 공통 부모에 `LobbyNavigation`을 붙여 홈의 열기 버튼, 목적지 화면의 돌아오기 버튼과 그 화면을 각각 연결한다. 화면 시작 시 `Initialize(tryEnterDestination)`로 목적지별 승인 콜백을 넘긴다. [KAN-171](https://gametithe.atlassian.net/browse/KAN-171)

모의 서비스가 열기를 수락하면 해당 화면만 표시된다. 거절하면 `OpenRejected`에 목적지를 전달해 호출자가 오류를 안내할 수 있고 홈을 계속 사용한다. 목적지에서 돌아오면 같은 홈을 다시 표시한다. 비활성 화면에서는 버튼 입력을 처리하지 않는다.

Unity Test Runner PlayMode의 `LobbyNavigationTests`에서 다섯 열기 버튼과 돌아오기 버튼, 모의 서비스 거절 후 재시도, 구성 누락과 중복을 확인한다.
