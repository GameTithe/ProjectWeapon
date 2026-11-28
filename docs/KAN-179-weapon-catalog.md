# 무기 도감 목록

기획서 v3.0 4~7쪽의 1~5등급 21종을 등급과 기획서 순서대로 표시한다. 저장된 도감 진행은 `CatalogProgress.GetStage`로 읽는다. 미발견은 물음표, 발견은 분류별 픽셀 실루엣, 회수와 숙련은 이름과 등급 색이 보인다. 인식할 수 없는 저장 상태는 `기록 확인 필요`로 표시한다. 도감 화면을 열고 닫아도 저장과 무기 소유권은 갱신하지 않는다. [KAN-179](https://gametithe.atlassian.net/browse/KAN-179) · [KAN-49](https://gametithe.atlassian.net/browse/KAN-49)

획득처는 기획서의 구역 탐험과 코인·광고 뽑기 경로를 사용한다. 나무 창은 현재 검증판의 폐성 1구역 상자, 전설 3종은 기획서에 지정된 상위 구역 탐험으로 표시한다. 나머지 개별 구역, 서버 제공 시작 여부나 드롭률은 목록 데이터에 가정해서 넣지 않는다. 실제 지급 여부는 해당 콘텐츠와 거래가 결정한다. [KAN-192](https://gametithe.atlassian.net/browse/KAN-192) · [KAN-69](https://gametithe.atlassian.net/browse/KAN-69)

현재 병합된 실물 원형과 열려 있는 콘텐츠 PR의 ID를 사용한다. 번개검 `lightning_sword`, 천벌의 검 `heavens_wrath_sword`, 창세의 활 `genesis_bow`는 원형이 미등록인 상태에서 이름 규칙에 맞춰 예약한 키다. 해당 원형이 추가되면 같은 키를 사용해야 진행 상태가 연결된다. [KAN-151](https://gametithe.atlassian.net/browse/KAN-151) · [KAN-165](https://gametithe.atlassian.net/browse/KAN-165) · [KAN-169](https://gametithe.atlassian.net/browse/KAN-169)

## 확인 방법

Unity에서 `Assets/Scenes/SortieLoop/SortieLoop.unity`를 실행하고 폐성 로비에서 **도감**을 누른다. 저장 자료를 다시 읽을 수 있도록 목록을 열 때 디스크 스냅샷을 확인한다. 현재 전투용 원형에 없는 등의 이유로 장착에 쓰이지 않는 기록도 유효한 저장 파일에서 실물 생성 없이 읽는다. 닫으면 같은 로비로 돌아온다.

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.CatalogUI.Tests.CatalogListPanelTests --output "$env:TEMP/KAN-179-results.xml"
```

테스트는 읽을 수 없는 저장, 새로 연 저장의 현재 상태, 21종과 등급별 개수, 네 표시 단계와 알 수 없는 상태, 스크롤과 닫기, 저장 파일 바이트 보존을 확인한다. 전체 PlayMode의 `RealCatalogButtonReadsTheSavedRosterAndReturnsWithoutWriting`는 실제 출격 로비의 버튼과 저장 경로를 사용한다.
