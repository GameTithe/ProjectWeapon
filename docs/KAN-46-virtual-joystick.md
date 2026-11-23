# KAN-46 가상 조이스틱 이동

[Jira KAN-46](https://gametithe.atlassian.net/browse/KAN-46)

## 에디터 재현

1. 임시 씬에 XY 평면을 바라보는 Orthographic Camera와 Screen Space - Overlay Canvas를 만든다. Canvas의 `GraphicRaycaster`를 활성화한다.
2. 씬의 EventSystem에 `InputSystemUIInputModule`을 사용한다. 모듈의 Actions Asset은 기존 `Assets/Settings/InputSystem_Actions.inputactions`를 지정하고, Point는 `UI/Point`, Left Click은 `UI/Click`을 연결한다. Pointer Behavior는 `Single Mouse Or Pen But Multi Touch And Track`을 사용한다.
3. `Assets/ProjectWeapon/Movement/VirtualJoystick.prefab`을 Canvas 아래에 넣는다. `PrototypeMovementPlayer.prefab`을 월드에 넣고 인스턴스의 `PlayerMovement` 컴포넌트에서 Joystick 슬롯으로 앞의 인스턴스를 연결한다. 런타임 조립에서는 `BindJoystick`으로 같은 참조를 전달한다.
4. Play에서 조이스틱을 마우스로 누르거나 터치하여 움직인다. 중앙 부근에서는 멈춰 있고, 가장자리에서 더 바깥으로 드래그해도 최대 속도를 유지한다. 버튼 등 다른 UI의 터치로는 이동하지 않는다. 소유한 손가락을 떼면 다음 물리 스텝에서 정지한다. 조이스틱을 누르고 있지 않을 때는 프로젝트의 기존 WASD, 방향키, 게임패드 이동 입력도 확인할 수 있다.

`PrototypeMovementSettings.asset`에는 최고 속도 **4 유닛/초**와 반경 기준 데드존 **0.15**를 임시 값으로 지정했다. 조이스틱은 패드 중앙에서 현재 포인터까지의 방향을 사용한다. 데드존 바깥의 크기는 0부터 1까지 선형으로 환산한다. 데스크톱과 게임패드 입력은 KAN-195의 프로젝트 공용 Move 액션을 그대로 읽는다. 조이스틱이 터치를 소유하는 동안에는 중앙을 눌러도 기존 Move 값으로 넘어가지 않는다.

첫 포인터가 조이스틱 영역에서 눌러야 소유권을 얻는다. 다른 UI나 같은 조이스틱을 추가로 누른 손가락을 움직이거나 떼어도 첫 포인터에 영향이 없다. 소유권이 끝나면 다른 손가락이 새로 눌러야 조이스틱을 잡을 수 있다. 취소, 오브젝트 비활성화, 앱 포커스 손실 및 일시정지는 소유권을 해제한다. 팝업 전환 시 조이스틱을 함께 숨기거나 `Release()`로 기존 소유권을 해제할 수 있다.

## 검증

Unity 6000.6.9f1에서 다음 명령을 실행한다.

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.Movement.Tests.VirtualJoystickPlayModeTests --output "$env:TEMP/KAN-46-movement/playmode.xml" --timeout 900 --format json --non-interactive
```

PlayMode 테스트는 에디터에서 두 프리팹을 실제로 불러오고, Input System의 가상 터치와 키보드 이벤트를 기존 액션 에셋 및 uGUI 이벤트 모듈로 통과시킨다. 다른 버튼의 클릭, 두 번째 손가락의 이동 및 해제, 경계 밖 드래그, 데드존, Rigidbody2D 위치와 정지, 터치 취소, 비활성화, KAN-195 입력 복귀를 검사한다.

로컬 PlayMode 결과는 3건 통과, 실패와 건너뜀은 각각 0건이다.
