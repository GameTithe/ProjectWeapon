# KAN-170 출격 장비 선택과 손실 안내

[Jira KAN-170](https://gametithe.atlassian.net/browse/KAN-170) · [KAN-45 소유권 결정](https://gametithe.atlassian.net/browse/KAN-45) · [KAN-85 출격 명세](https://gametithe.atlassian.net/browse/KAN-85)

## 씬에 연결

1. 로비 Canvas와 GraphicRaycaster, EventSystem을 사용한다. Canvas 아래에 화면을 채우는 RectTransform을 만들고 `SortieEquipmentPanel`을 붙인다. 검증 기준은 960×540 가로 Canvas이다.
2. `Initialize(inventory, catalog, font, available, confirm)`에 동일한 로비 인벤토리와 무기 카탈로그, TMP 폰트, 출격 가능 여부와 시작 함수를 전달한다. 기본 글꼴은 `Assets/ProjectWeapon/SortieUI/SortieHangul.asset`을 사용한다. 프로젝트에 TMP Essential Resources가 있어야 한다. 현재 원형 무기 카탈로그 밖의 이름을 표시하는 경우 해당 글자를 포함한 폰트를 전달한다.
3. 로비 출격 버튼에서 `Open()`을 호출한다. 구역·가능 상태·인벤토리를 외부에서 바꾸면 `Refresh()`를 호출한다.
4. `confirm`은 현재 구역과 탐험 가능 상태를 재확인하고 같은 로비 인벤토리를 쓰는 `SortieStarter.TryStart(runId, zoneId)`의 결과를 반환한다. `Confirmed`는 성공과 실제 반입 명세를 전달한다. `Dismissed`는 닫기 입력을 전달한다.

장착·빼기는 창고와 장착 칸 사이의 준비 상태를 즉시 바꾸고, 창을 닫아도 준비한 장착을 유지한다. 출격 확정은 호스트가 수행한다. 운반 칸은 전리품 전용이며, 내용물이 감지되면 출격을 잠근다. 광고·코인 경로는 반입 손실 기준에 영향을 주지 않는다.

빈 출격이면 첫 칸에 무료 낡은 검을 미리 표시한다. 화면을 열고 닫는 것만으로 대여 실물을 발급하지 않는다. 통합 호스트(KAN-193)가 수락 가능한 요청에 한해 동일한 탐험 ID의 `PrepareStarterLoan(runId.Value)`과 `TryStart`를 연결한다. 준비 이후 거절·예외가 생기면 해당 ID로 재시도하거나 호스트가 준비 상태를 정리한다. UI는 거절 상태를 표시하고 출격 가능 상태로 돌아오면 재시도를 받는다.

## 검증

Unity Test Runner의 PlayMode에서 `ProjectWeapon.SortieUI.PlayMode.Tests`를 실행한다. 테스트는 실제 카탈로그, 인벤토리와 구역 출격 구현을 연결한다.

- 창고 무기를 두 칸에 배치하고 장착 해제 후 재장착한다. 출격 명세에서 선택한 두 실물과 순서를 확인한다. 세 번째 무기는 창고에 남고 중복 확인은 한 번만 수락된다.
- 출격 가능 여부가 꺼졌거나 이전 렌더 이후 운반품이 들어오면 시작 호출이 차단된다. 실제 탐험 시작이 거절되면 창이 열린 상태로 유지되며 장착과 창고 위치가 보존된다.
- 무장 없이 닫으면 인벤토리가 비어 있다. 빈 장착으로 출격하는 경우 호스트가 낡은 검을 한 번 대여하고 판매·창고 보관 제한을 유지한다.

[한글 폰트 원본](https://github.com/notofonts/noto-cjk/blob/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Sans/OTF/Korean/NotoSansCJKkr-Regular.otf) · [SIL OFL](../Assets/ProjectWeapon/SortieUI/SortieHangul-OFL.txt)
