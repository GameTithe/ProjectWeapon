# KAN-113 낡은 검 실물 숙련

[KAN-113](https://gametithe.atlassian.net/browse/KAN-113) · [KAN-49](https://gametithe.atlassian.net/browse/KAN-49) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

출격이 확정되면 현재 `RunState`, 해당 출격 명세, 출격 시점의 확정 저장본으로 `OldSwordMasteryTracker`를 만든다. 기존 회수 상태인 `old_sword` 중 반입한 개인 무기 실물 ID를 그 런의 후보로 고정한다. 대여품은 제외한다. 요구 마지막 타격 수는 기본값 3이며 생성 시 과제 설정으로 전달할 수 있다.

전투 호스트는 대상이 실제로 사망한 유효한 피해 접수 후, 해당 공격이 발동할 때 포착한 무기 실물 ID와 안정적인 적 실물 ID, 런 ID 및 피해 유효 시각으로 `ObserveFinalBlow`를 호출한다. 같은 적의 두 번째 관측은 무시한다. 다른 동일 원형 실물의 처치는 별도로 집계한다. 종료된 런, 다른 런, 제한 시각 밖 관측은 진척에 반영하지 않는다. 귀환 확정 시각까지 수락된 사건만 평가한다.

보유품과 지갑을 전달하는 기존 `RunSettlementCoordinator.TrySettle`의 다섯 번째 인수로 그 런의 트래커를 넘긴다. 성공 정산에서 기존 복구로 인정된 항목 중 처치 조건을 달성한 동일 실물이 있을 때 `CatalogProgress.RecordVerifiedMastery`를 후보 스냅샷에 적용한다. 새 회수를 기록하기 전에 숙련을 확인하므로, 첫 회수와 숙련이 같은 성공에서 동시에 확인되는 경우를 막는다. 정산 영수증, 창고, 확정 코인과 숙련은 한 번에 저장한다.

실패한 과제 진행은 다음 런에 전달하지 않는다. 저장 충돌 또는 응답 유실은 같은 결과 및 증거로 재호출한다. 기존 영수증이 있으면 그대로 반환한다. 다른 과제는 `IRunMasteryObjective`의 읽기 전용 평가 경계에 연결할 수 있다. 라이브 전투 호스트의 사건 연결과 다른 과제 콘텐츠는 후속 범위다.

## 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode editor --filter OldSwordMasterySettlementTests
```

테스트는 실제 출격 예약, RunState, 정산 코디네이터와 임시 파일의 원자 저장을 사용한다. 성공 후 숙련, 대여·최초 회수·다른 실물·적 ID 중복·처치 시각·사망·제한시간·런 간 누적, 저장 응답 유실 재호출과 기존 영수증 보존을 검증한다.
