# 앱 라이프사이클과 탐험 복구

승인된 A는 앱이 백그라운드에 있거나 연결이 끊겨도 최초 제한시간을 적용한다. 같은 `RunId`, 구역, 시작 시각, 만료 시각을 복구한다. 중단 시 진행 중인 귀환 채널은 취소하며, 재시도는 3초부터 시작한다. 중단 프레임에 먼저 유효해진 결과는 기존 단일 결과 판정 순서로 처리한다. 정확히 만료 시각에 도달하면 시간 초과가 우선한다. [KAN-189](https://gametithe.atlassian.net/browse/KAN-189) · [KAN-47](https://gametithe.atlassian.net/browse/KAN-47) · [KAN-188](https://gametithe.atlassian.net/browse/KAN-188)

## 연결 계약

- 호스트는 탐험을 소유한 `RunState`와 같은 `ExtractionChannel`을 사용해 `RunRecoveryCoordinator`를 만들고 `RunLifecycleRelay.Initialize`를 호출한다. 콜백에는 해당 프레임에서 이미 수락된 전투 신호와 현재 권위 시계의 검증 여부를 제공한다. `RunState`의 시계는 중단 시간까지 포함하는 단조 증가 시계여야 한다.
- `RunLifecycleRelay`는 Unity의 포커스, 일시정지, 종료 콜백과 호스트의 `SetConnectionAvailable` 통지를 받는다. 여러 중단 사유가 겹쳐도 최초 중단을 한 번 처리하며 모두 해소된 시점에 활성 탐험을 재접속한다. 호스트는 `IsGameplayBlocked`가 참인 동안 입력, 새 출격, 보상 확정을 막고 `RecoveryChanged`를 구독해 결과를 전달한다.
- 프로세스 재시작 시 호스트는 `Reconnect(runId, checkpoint)`를 호출한다. 계정 저장본이 정상이고 같은 `RunId`의 확정 영수증이 있으면 영수증을 그대로 반환한다. `Settled`에서는 영수증과 해당 저장 리비전으로 결과를 표시한다. `Finalized`에는 `RunResult`가 있으며 검증된 동일 런 자료를 가진 기존 정산 호출자가 처리한다. 복구 모듈의 계정 저장 접근은 읽기 전용이다. [KAN-94](https://gametithe.atlassian.net/browse/KAN-94) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)
- 영수증이 없는 새 프로세스에서 `VerifiedRunRecoveryCheckpoint`를 만들려면 호스트가 같은 저장 리비전의 전체 탐험 체크포인트와 원래 탐험 시간축의 현재 시각을 검증해야 한다. `RunState.GetRecoveryCheckpoint`는 시간축, 결과, 마지막 귀환 시도 ID를 캡처한다. HP, 적 상태, 장비, 임시 코인, 전리품, 난수 진행 상태는 탐험 체크포인트를 소유한 호스트가 같은 버전으로 제공한다. `OnApplicationQuit`를 받지 못한 경우 마지막 검증 가능한 체크포인트나 확정 영수증을 사용한다.
- 저장본 누락, 백업만 읽힌 경우, 알 수 없는 영수증, 시계·체크포인트 검증 실패는 `RecoveryPending`이다. 보유 자산과 출격 예약을 보존한다. 실행 씬과 전체 체크포인트 제공자 연결은 후속 호스트 통합에서 처리한다. [KAN-53](https://gametithe.atlassian.net/browse/KAN-53) · [KAN-91](https://gametithe.atlassian.net/browse/KAN-91) · [KAN-193](https://gametithe.atlassian.net/browse/KAN-193)

## 검증

```powershell
unity test . --mode EditMode --output "$env:TEMP/KAN-189-editmode.xml" --timeout 300
unity test . --mode PlayMode --output "$env:TEMP/KAN-189-playmode.xml" --timeout 300
```

PlayMode의 `RunLifecycleRecoveryTests`가 Unity 컴포넌트의 포커스·백그라운드 메시지와 `Reconnect` 진입점을 실행한다. 종료 검증은 테스트 러너 프로세스를 보존하기 위해 종료 콜백을 직접 호출한다. EditMode 전체에서 기존 런·귀환·저장 테스트를 실행한다.

1. 1초에 시작해 11초에 만료되는 런에서 4초에 귀환을 시작하고 5초에 앱을 백그라운드로 전환한다. 포커스 손실과 연결 단절이 겹친 상태에서 마지막 중단 사유를 해소하고 8초에 재개한다. 신규 채널의 시작부터 완료까지 3초이며 이전 채널의 완료 콜백은 적용되지 않는다.
2. 살아 있는 프로세스와 새 프로세스에서 각각 만료 이전, 정확한 만료 시각, 만료 이후 복귀를 재현한다. 원래 기한과 결과의 유효 시각을 비교한다. 중단과 채널 완료가 같은 시각이면 취소하며, 더 이른 채널 완료와 유효한 사망 신호는 기존 판정 규칙을 사용한다.
3. 실제 KAN-96 트랜잭션을 커밋하고 새 `RunState`에서 같은 `RunId`로 10회 다시 접속한다. 영수증, 코인, 리비전과 저장 파일 바이트가 동일해야 한다.
4. 시계 역행, 미검증 시간, 누락된 체크포인트, 다른 런·리비전, 만료 시각의 잘못된 성공 기록, 누락 또는 백업만 남은 계정 저장을 주입한다. 복구 대기와 저장 파일 보존을 확인한다.
