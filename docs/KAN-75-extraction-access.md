# KAN-75 귀환진 접근 판정과 시작 가용 상태

`ExtractionAccess`를 기존 `RunState`와 귀환진의 구역 ID·월드 XY 중심·반경으로 구성한다. 반경은 양의 유한한 값이며 호출부가 공급한다. 플레이어 위치와 중심의 제곱거리가 반경의 제곱 이하일 때 경계를 포함한다. [KAN-75](https://gametithe.atlassian.net/browse/KAN-75) · [KAN-46](https://gametithe.atlassian.net/browse/KAN-46)

1. 표시를 갱신할 때 현재 탐험 ID와 플레이어의 실제 월드 XY 위치로 `CanStart`를 확인한다. 기존 이동 컴포넌트가 움직이는 `Rigidbody2D.position`을 위치 입력으로 사용할 수 있다.
2. 버튼을 한 번 누를 때 최신 위치와 같은 탐험 ID를 `TryStart`에 전달한다. 위치와 귀환 가능 상태를 다시 판정하므로 표시 갱신 뒤 반경을 벗어나면 요청이 거부된다. 한 번 수락하면 RunState가 귀환 중으로 전이하며 다음 요청은 거부된다.
3. UI와 VFX는 각각 같은 가용 결과를 사용한다. 예를 들어 기존 `ExtractionFeedback.SetAvailability`에는 `CanStart`의 결과를 전달할 수 있다. 시작이 수락되면 반환된 시도 ID와 시작·완료 예정 시각을 표시하는 쪽에서 읽는다. [KAN-30](https://gametithe.atlassian.net/browse/KAN-30)

시작 가용 조회와 시작 요청은 동일한 RunState 단계·탐험 ID·만료 검사 규칙을 공유한다. 만료 이전에는 기존 계약대로 시작 가능하며, 3초 채널링 완료와 시간초과의 경합은 RunState가 판정한다. 채널링 진행률과 완료 신호는 KAN-83, 이동·피해에 의한 취소는 KAN-81과 KAN-190에서 연결한다. [KAN-188](https://gametithe.atlassian.net/browse/KAN-188) · [KAN-83](https://gametithe.atlassian.net/browse/KAN-83) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

## 재현 절차

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Extraction.Tests.ExtractionAccessTests --output "$env:TEMP/KAN-75-results.xml"
```

| 조건 | 기대 결과 |
| --- | --- |
| 중심 `(10, -3)`, 반경 5에서 `(16, -3)` → `(12, -2)` → `(13, 1)` → `(13, 1.001)` | 가용 결과가 거짓, 참, 참, 거짓으로 바뀐다. 조회 중에는 시도가 생성되지 않는다. |
| 경계에서 버튼을 두 번 누름 | 첫 요청만 시도 한 건을 반환한다. |
| 다른 구역·탐험 ID, 잘못된 월드 위치, 만료 시각 또는 탐험 중이 아닌 상태 | 시작을 거부하며 다른 탐험이나 기존 귀환 상태를 바꾸지 않는다. |
| 이전 시도가 명시적 취소로 종료된 후 반경 안에서 버튼을 누름 | 새 시도 ID를 발급한다. |
