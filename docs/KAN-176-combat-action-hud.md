# KAN-176 전투 액션 HUD

960×540 기준의 화면 Canvas에 `CombatActionHud.Create(canvas, joystickPrefab, hangulFont)`로 붙인다. 장면의 기존 `GraphicRaycaster`와 `InputSystemUIInputModule`을 공용으로 사용한다. 화면 안전 영역에만 조작부를 배치하고, 터치 면을 제외한 그래픽은 월드 입력을 차단하지 않는다. [KAN-176](https://gametithe.atlassian.net/browse/KAN-176) · [KAN-46](https://gametithe.atlassian.net/browse/KAN-46) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

- 왼쪽 아래는 기존 가상 조이스틱과 실제 플레이어 이동을 사용한다. 오른쪽 아래는 스킬, 회피, 귀환이며 장착 전환 두 칸은 하단 중앙에 있다. 액션 버튼은 52 Canvas 설계 단위이며, 주홍·청색·아이보리 픽셀 아이콘은 프로젝트의 `16비트 던전 전투와 푸른 포털.png`를 참고해 새로 제작했다.
- 현재 장착 스킬이 방향형이면 16 설계 단위 이상 드래그하여 발동한다. 자신형이면 누른 포인터를 같은 버튼 안에서 떼어 발동한다. 입력 중단, 캔슬 터치, 버튼 바깥 release와 바뀐 장착 실물은 남은 조작을 취소하거나 실행기에서 거절된다. 스킬이 없거나 지원하지 않는 조준 형식은 비활성화된다.
- 회피는 포인터를 누르는 시점에 `PlayerMovement.TryDodge`를 호출한다. 귀환은 현재 접근 범위, 상태와 승인 이동 입력을 확인한다. 이동 입력 크기가 0.20 이상이면 귀환을 시작하지 않는다. 사용 가능 여부는 탐험 시각, 실제 장착, 쿨다운과 오버레이·복구 상태에 따라 갱신된다. [KAN-56](https://gametithe.atlassian.net/browse/KAN-56) · [KAN-75](https://gametithe.atlassian.net/browse/KAN-75) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

## 장면에서 연결

`CombatHudBindings`에 같은 탐험의 `RunState`, 플레이어와 해당 이동 설정, 현장 인벤토리, `EquippedWeaponSelection`, 수동 실행기, 탐험 시계, 입력 가용 및 귀환 가능·실행 콜백을 넘긴다. HUD는 씬·어댑터 간 슬롯이나 자체 쿨다운 저장을 생성하지 않는다. 회피 버튼 표시는 `DodgeStarted`와 같은 이동 설정을 사용한다. 복구 UI나 창 전환이 입력을 막는 순간 호스트는 `Refresh()`를 한 번 호출할 수 있다. HUD는 귀환 진행과 결과 UI를 추가하지 않는다.

폐성 통합 시에는 기존 키보드 회피와 HUD 회피를 공유 `PlayerMovement.DodgeStarted`로 관측하고 해당 신호를 기존 프레임의 `ExtractionChannel.ObserveAcceptedDodge`와 함께 처리한다. 이를 통해 터치 회피의 귀환 취소와 기존 타격 우선순위를 유지할 수 있다. HUD 테스트도 이 계약을 사용한다. 실제 개별 무기 효과는 같은 수동 실행기의 별도 효과 모듈이다.

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.CombatHud.Tests.CombatActionHudTests --output "$env:TEMP/KAN-176-hud.xml" --timeout 300
```

실제 uGUI 레이캐스터 및 Input System 터치 두 개, 가상 조이스틱 프리팹과 Rigidbody2D, 무기 선택·수동 스킬·귀환 API를 독립 전투에 연결한다. 이동과 조준 동시 입력, 방향형과 자신형 전환, 취소와 쿨다운, 귀환진 범위와 이동 제한, 회피 입력 시점, 모서리 안전 영역과 UI 오버레이를 확인한다. 기기 DPI와 컷아웃의 실기기 확인은 KAN-39에 남긴다. [KAN-39](https://gametithe.atlassian.net/browse/KAN-39)
