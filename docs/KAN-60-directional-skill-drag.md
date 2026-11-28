# 방향형 스킬 드래그와 취소

오른쪽 스킬 버튼에 `DirectionalSkillDrag`를 붙인다. 버튼의 `Graphic`은 기존 Canvas의 레이캐스트를 사용한다. 호스트는 같은 루트 Canvas의 조준 허용 `RectTransform`과 `DirectionalSkillArrow`에 `Bind`로 연결하고, 현재 `EquippedWeaponSelection`, KAN-58 실행기, 런 시계와 플레이어 월드 좌표를 제공한다. 화살표는 포인터 레이캐스트를 받지 않으며, 허용 영역은 기존 안전 영역·조작 레이아웃에서 지정한다. [KAN-60](https://gametithe.atlassian.net/browse/KAN-60) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58)

- 버튼에서 눌린 포인터, 시작 위치, 장착 실물 ID와 제스처 키를 잡는다. 같은 포인터의 드래그만 화살표를 갱신한다. 다른 포인터의 왼쪽 조이스틱 입력은 계속 해당 조이스틱이 처리한다.
- 루트 Canvas 설계 좌표에서 시작 위치와 놓는 위치의 거리가 **16단위 이상**이고 놓는 점이 조준 영역에 있으면 발동을 요청한다. 16단위 미만이거나 영역 밖이면 취소한다. 2배 Canvas에서 31px는 취소, 32px는 유효 방향이다. 거리는 `minimumCanvasDistance`에 저장하는 검증 가안이다.
- 놓는 프레임의 손가락 방향과 플레이어 좌표를 전달한다. KAN-58은 실제 런 상태, 캡처한 장착 실물, 쿨다운과 방향을 다시 확인한다. 유효한 제스처가 접수되면 같은 버튼의 일반 클릭 경로를 제외하고 제스처를 실행기에서 한 번만 소비한다.
- 창 닫기·런 전환으로 이 버튼이나 입력 경로가 중단될 때 호스트는 `SetInputAvailable(false)`를 호출할 수 있다. 버튼 비활성화, 앱 포커스 손실·일시 중지, 입력 기기 끊김·리셋과 소유 터치 취소는 조준 화살표와 제스처를 정리한다. UI가 돌아오면 새로운 눌림이 필요하다.

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.ManualSkillUI.Tests.DirectionalSkillDragTests --output "$env:TEMP/KAN-60-directional-playmode.xml" --timeout 300
```

테스트는 실제 `InputSystemUIInputModule`, 다중 터치 입력, uGUI 버튼·Canvas의 화살표 메시, KAN-58 실행기와 기존 왼쪽 조이스틱 프리팹·이동 Rigidbody2D를 연결한다. 경계 직전·정각, 두 손 동시 입력과 다른 포인터 해제, 마지막 위치 방향, 영역 밖·원점 복귀·기기 터치 취소, 중단 뒤 지난 손가락 놓기, 같은 원형의 다른 장착 실물로 교체를 재현한다. 검증용 스킬 객체는 테스트에만 있다. [Unity Canvas 좌표 변환](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/recttransformutility/screenpointtolocalpointinrectangle) · [Unity Canvas 메시](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/canvasrenderer/getmesh)
