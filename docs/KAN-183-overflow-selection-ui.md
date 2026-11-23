# KAN-183 가방 초과 획득 선택 UI

가방이 찬 동안 새 무기와 기존 운반 무기의 이름, 등급, 종류를 비교한다. 교체 후보를 선택해도 물품은 그대로이며 **교체 확정**에서만 획득 요청을 확정한다. **취소**는 두 물품을 그대로 둔다. [KAN-183](https://gametithe.atlassian.net/browse/KAN-183) · [KAN-66](KAN-66-loot-pickup-trigger.md) · [KAN-80](KAN-80-inventory-capacity.md)

## 구성

1. 게임 씬의 화면 공간 Canvas와 터치용 EventSystem을 사용한다. Canvas에는 `GraphicRaycaster`와 화면 크기 기준 `CanvasScaler`를 붙인다. EventSystem의 입력 모듈은 프로젝트 Input System에 맞춘다.
2. Canvas 안의 전체 영역을 덮는 RectTransform에 `LootOverflowPanel`을 추가한다. 다른 HUD보다 위에 그릴 수 있도록 배치하고, 화면 잘림을 피하려면 씬의 안전 영역 하위에 둔다. 호스트를 활성 상태로 유지하면 요청이 올 때 패널만 나타난다.
3. 플레이어의 `LootPickupTrigger`, 프로토타입 `PrototypeStarterWeaponCatalog`, 실제 한글 글리프를 포함하는 `TMP_FontAsset`을 할당한다. 프리팹에서 인스펙터로 지정하거나 `Initialize(trigger, catalog, font)`를 호출한다. 현재 프로젝트에 TMP Essential Resources가 필요하며, 배포용 한글 글꼴은 승인한 자산으로 별도 연결한다. 이 티켓은 글꼴·TMP 리소스·캔버스를 저장소에 추가하지 않는다.
4. `ChoiceRequired`의 전리품 객체와 선택지를 표시한다. 범위를 벗어나면 기존 요청을 취소하거나 범위에 돌아와 확인할 수 있다. 비교 데이터가 비어 있는 후보는 설명을 표시하고 교체 대상으로 선택하지 않는다. 오래된 가방 스냅샷은 확정할 수 없으며 닫은 다음 새 요청으로 시도한다.

후보 선택과 확인은 선택지의 정확한 실물 ID를 사용한다. 기존 전리품 트리거의 `PickedUp` 결과에 담긴 새 무기와 `Displaced`의 월드 처리는 호출자가 연결한다. UI는 월드 오브젝트 삭제, 줍기 거리, 반입 또는 정산을 처리하지 않는다.

## 검증 재현

Unity 프로젝트 루트에서 실행한다.

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.LootUI.PlayModeTests --output "$env:TEMP/KAN-183-playmode.xml" --timeout 900
```

PlayMode 테스트는 격리된 2D 씬에 전리품 트리거와 Canvas를 만들고, UI 이벤트 경로로 버튼을 실행한다. 테스트에 TMP Essentials가 없는 경우 기존 uGUI 패키지의 공식 가져오기 API로 임시 설치하고 테스트가 만든 폴더만 정리한다. 테스트 실행 시스템에는 맑은 고딕, Noto Sans CJK KR 또는 Apple SD Gothic Neo와 같은 한글 지원 글꼴이 필요하다. 로컬 시스템 글꼴은 테스트용이며 게임 자산에 넣지 않는다.


## 이번 작업의 검증 결과

2026-11-23 Unity 6000.6.9f1과 Unity CLI 1.0.1에서 PlayMode 3건이 모두 통과했다. 트리거로 패널을 열고 Canvas 레이캐스트로 대상 버튼을 찾아 클릭했다. 이름·등급·종류 비교, 확정 전 기존품 보존, 비교 정보 누락 시 취소, 가방 변경 시 오래된 선택 차단을 각각 확인했다. NUnit XML과 로그는 로컬 `%TEMP%/ProjectWeapon-KAN183-pointer-1795435364`에 남겼다.
