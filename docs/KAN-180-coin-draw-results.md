# KAN-180 코인 뽑기 결과 화면

`CoinDrawResultPanel`은 코인 뽑기의 저장 영수증을 읽어 1개 또는 10개 결과를 원래 지급 순서대로 표시한다. 같은 원형이 여러 번 나오면 지급된 실물마다 한 줄을 그린다. 창고 수량은 활성 스냅샷에서 각 지급 실물 ID의 현재 위치를 확인한 값이다. [KAN-180](https://gametithe.atlassian.net/browse/KAN-180) · [KAN-99](https://gametithe.atlassian.net/browse/KAN-99) · [KAN-109](https://gametithe.atlassian.net/browse/KAN-109)

## 연결

1. 기존 Canvas의 `RectTransform`에 패널을 추가하고 960×540 참조 해상도, GraphicRaycaster 및 EventSystem을 사용한다. TMP Essential Resources와 한글 글꼴을 준비한다.
2. `Initialize(coinDrawService, weaponCatalog, nextExclusive, hangulFont)`로 실제 거래 서비스를 연결한다. 내장 버튼이나 외부 UI에서 `TryDraw(1)`과 `TryDraw(10)`을 호출한다.
3. 요청을 수락하면 `RequestId`와 `RequestCount`가 결과 확인까지 남는다. 재시도가 필요하면 같은 요청 정보로 거래 서비스를 다시 호출한다. 화면이나 세션을 다시 조립할 때 이미 알고 있는 거래는 `TryResume(id, count)`로 복구한다.

확정 결과를 확인하기 전의 중복 호출은 하나의 요청만 처리한다. 잔액 부족과 요청 충돌은 지급 목록 없이 표시한다. 저장 상태가 경합하거나 결과가 확인되지 않으면 같은 ID 재시도만 제공한다. 재조회 시 이미 커밋된 영수증은 다시 차감하거나 지급하지 않는다. [KAN-73](https://gametithe.atlassian.net/browse/KAN-73)

결과 안내는 뽑기 획득이 발견 조건이라는 점과, 회수 등록에 실제 무기를 소지한 성공 귀환이 필요하다는 점을 구분한다. [KAN-49](https://gametithe.atlassian.net/browse/KAN-49)

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.CoinDrawUI.Tests --output "$env:TEMP/KAN-180-playmode.xml"
```

테스트는 실제 Canvas, 버튼과 임시 저장소에서 1·10개 결과 순서, 중복 호출, 창고로부터 이동한 실물의 재조회, 저장 리비전 경합 뒤 같은 ID 재시도, 잔액 부족을 검증한다.

Unity 6000.6.9f1에서 해당 PlayMode 3건이 통과했다.
