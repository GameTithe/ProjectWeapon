# KAN-175 창고 목록과 장착·판매

[KAN-175](https://gametithe.atlassian.net/browse/KAN-175) · [KAN-101](https://gametithe.atlassian.net/browse/KAN-101)

960×540 기준 uGUI 창고에는 미반입 보관 무기, 장착 슬롯 두 개, 확정 코인이 표시된다. 창고 무기를 선택한 뒤 빈 슬롯을 누르면 장착하고, 장착 무기를 선택해 창고에 넣을 수 있다. 대여품은 장착 위치에 별도로 표시되며 보관과 판매 행동을 사용할 수 없다.

## 호스트 연결

1. 기존 Canvas의 콘텐츠 RectTransform에 `WarehousePanel`을 붙인다. 호스트 Canvas에는 `GraphicRaycaster`와 씬의 활성 `EventSystem`이 필요하다. 한국어 화면은 `WarehouseHangul.asset` 또는 해당 화면 및 무기 이름의 글자를 지원하는 TMP 글꼴을 넘긴다.
2. `Initialize`에 실제 `WeaponCatalog`, 판매 가격 에셋, 타운 `RunState`, 현재 확정 저장을 읽는 함수, 장착 변경을 저장하는 함수와 `WeaponSaleService.TrySell`을 전달한다. 판매 서비스와 화면은 동일한 카탈로그 및 가격 에셋을 사용한다.
3. 장착 변경은 화면에서 `InventoryService.Move`로 검증한 위치 목록을 받는다. 호스트는 주어진 저장본 리비전으로 현재 저장 계약에 따라 위치를 확정하고 저장된 스냅샷을 반환한다. 다른 저장 항목을 유지한다. 충돌 시 `null`을 반환하면 화면이 최신 저장본을 다시 읽는다.
4. 로비에서 `Open`으로 열고, 외부 거래 후 `Refresh`로 최신 화면을 읽는다. 성공한 판매 결과의 `ActiveSnapshot`과 해당 영수증 가격을 표시한다. 확인 뒤 가격이나 실물 위치가 바뀌면 다시 확인받는다. 저장 응답이 유실되면 같은 판매 요청 ID로 재시도한다.

## 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode playmode --async-tests true --filter WarehousePanelTests
unity command test status --project-path .
```

실제 uGUI 레이캐스트와 임시 파일 저장으로 장착·보관, 쓰기 충돌 재시도, 취소, 대여품 차단, 판매 중복, 변경된 가격·실물 위치와 응답 유실을 검증한다. 화면은 현재 프로젝트의 한국어 SDF 라이선스와 같은 방식으로 Noto Sans CJK KR Regular에서 필요한 글자만 포함한다. 기존 씬의 로비 진입 버튼과 최종 아트는 후속 범위다.
