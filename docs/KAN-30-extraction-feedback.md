# KAN-30 귀환 시각 피드백

귀환진을 월드 좌표의 외곽선으로 표시한다. 귀환이 가능하면 선이 밝아지고 두꺼워진다. 채널링 중에는 위쪽에서 시작하는 바깥 고리가 경과 시간에 따라 차오른다. 취소되면 진행선이 즉시 사라진다. 성공이 확정되면 위쪽에 완료 표시가 나타나고 다음 탐험이 시작될 때 지워진다. [KAN-30](https://gametithe.atlassian.net/browse/KAN-30)

## 연결 방법

1. 귀환진 중심에 `ExtractionFeedbackView`를 붙이고 같은 위치·방향·단위 스케일을 가진 하위 객체의 `LineRenderer` 세 개를 Area Outline, Progress Arc, Completion Mark에 연결한다. 부모와 하위 객체는 로컬 XY 평면을 사용한다. 곡선 폭은 균일하게 두고 정렬 순서는 지면보다 위에 둔다.
2. 각 선에 기존 URP 2D의 Sprite Unlit Default처럼 정점 색상을 사용하는 비조명 재질을 할당한다. 이 티켓의 흰색과 회색, 원과 체크는 검증용 임시 표현이다. [KAN-21](https://gametithe.atlassian.net/browse/KAN-21)
3. 탐험을 관찰하는 조정 계층에서 `ExtractionFeedback` 인스턴스를 사용한다. 모니터링 중에는 같은 탐험 시계의 현재 값으로 `Sample(now)`를 읽고 `view.Render(snapshot, radius)`를 호출한다. 귀환진 반경은 귀환 가능 영역을 판정하는 쪽에서 전달한다.

| 연결할 신호 | 표시 계층 호출 |
| --- | --- |
| 유효한 `RunStarted` | `OnRunStarted(runId.Value)` |
| 귀환 가능 여부 변경 | `SetAvailability(runId.Value, canStart)` |
| 귀환 시작 상태 반영 | `OnExtractionStarted(runId.Value, attempt.Id, attempt.StartedAt, attempt.CompletesAt)` |
| 귀환 취소 상태 반영 | `OnExtractionCancelled(runId.Value, attemptId)` |
| `RunResultFinalized` | `OnRunResultFinalized(result.RunId.Value, result.Succeeded)` |

진행률은 주입된 귀환 시작부터 완료 예정 시각까지의 비율이다. 3초 채널링은 0초에서 0%, 1.5초에서 50%, 3초에서 100%로 표시한다. 시간이 차면 완료 신호를 기다린다. 취소 이후 이전 시도를 다시 받거나 다른 탐험의 결과를 받으면 현재 표시를 유지한다. 같은 탐험의 첫 결과만 받고 실패 시 진행선과 완료 표시를 숨긴다. 이 계층은 화면을 갱신하며 입력, 근접 판정, 타이머, 보상 확정은 각 게임 계약이 맡는다. [KAN-19](KAN-19-domain-contracts.md) · [KAN-75](https://gametithe.atlassian.net/browse/KAN-75) · [KAN-83](https://gametithe.atlassian.net/browse/KAN-83)

## 검증

Unity 프로젝트 루트의 PowerShell에서 실행한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.ExtractionPresentation.Tests --output "$env:TEMP/KAN-30-editmode.xml" --timeout 900
```

- 시작부터 3초까지의 진행률과 완료 신호 대기, 취소·시도 재전달, 중복 결과와 다음 탐험 초기화를 단위 테스트에서 재현한다.
- 격리된 월드 좌표에 실제 `LineRenderer`를 만든다. 외곽선·절반 진행선·완료 표시의 선분과 굵기를 기준으로 반경 90% 이내를 지나는 선이 없는지 확인한다. 월드 앵커 이동, 귀환 가능 표시의 선 굵기 변화, 취소 때 진행선 비활성화도 확인한다.
- 기존 게임 씬에 연결하거나 모바일 화면에서 최종 연출을 평가하는 작업은 담당 후속 티켓의 씬과 승인된 아트 규격을 사용한다.


## 이번 작업의 실행 결과

2026-11-23 Unity 6000.6.9f1 및 Unity CLI 1.0.1에서 모델과 LineRenderer의 EditMode 테스트 10건이 모두 통과했다. NUnit XML과 실행 로그의 로컬 위치는 `%TEMP%/ProjectWeapon-KAN30-1795428861`다.
