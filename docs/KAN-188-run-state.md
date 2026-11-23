# KAN-188 탐험 상태 전이 재현

탐험은 마을에서 시작하고, 탐험 중에 유효한 귀환 요청이 들어오면 3초 채널링을 진행한다. 이동 또는 피해의 취소 요청은 활성 시도 ID와 함께 전달한다. 사망, 시간초과, 귀환 완료는 한 프레임의 유효 시점과 함께 모아서 `RunState.ProcessSignals`에 전달한다.

귀환 진행 어댑터가 완료 신호를 넣는다. 프레임을 건너뛰더라도 완료 신호의 유효 시점은 `ExtractionAttempt.CompletesAt`이다. 각 프레임의 종료·취소 입력을 모아 한 번 판정한다. 다음 배치에서 직전 판정 시점보다 오래된 신호는 무시한다. 시계 값이 같아도 경합하는 신호는 같은 배치에 넣어야 한다. 아무 입력이 없는 프레임도 판정하면 만료 시점을 처리한다. 먼저 확정한 런 결과는 변경되지 않으며 이벤트는 한 번 발행된다. 정산 계층은 `RunResultFinalized`의 `RunId`, 양의 정수 구역 ID, 사유, 유효 시점과 잔여 시간을 사용할 수 있다. 소지품·전리품 스냅샷과 재화 반영은 관련 정산 티켓에서 연결한다. [KAN-19](https://gametithe.atlassian.net/browse/KAN-19) · [KAN-188](https://gametithe.atlassian.net/browse/KAN-188) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

## 재현 절차

프로젝트 루트에서 Unity CLI로 EditMode 테스트를 실행한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Run.Tests --output "$env:TEMP/KAN-188-results.xml"
```

| 조건 | 기대 결과 |
| --- | --- |
| 만료 10초, 귀환 시작 6.999초 | 9.999초의 성공이 확정된다. 같은 프레임 내 시간초과 이벤트의 입력 순서는 영향을 주지 않는다. |
| 만료 10초, 귀환 시작 7.000초 또는 7.001초 | 시간초과로 한 번만 실패한다. |
| 채널링 중 취소한 뒤 새 귀환 시도 시작 | 탐험 상태로 복귀하고 이전 시도의 늦은 완료 및 취소 신호는 새 시도에 영향을 주지 않는다. |
| 귀환 완료와 사망의 입력 순서 변경 | 먼저 도달한 유효 종료 결과가 확정된다. 동일 유효 시점에는 프로토타입 검증 순서에 따라 사망을 반영한다. |
| 사망 또는 성공 이후 뒤늦은 종료 요청 | 최초 결과를 반환하며 최종 이벤트를 재발행하지 않는다. |

타이머와 귀환 진행률 표시는 어댑터가 연결한다. 이동의 유효 취소 기준과 앱 종료·재접속 시계 정책은 해당 결정 티켓에서 정한다. [KAN-47](https://gametithe.atlassian.net/browse/KAN-47) · [KAN-83](https://gametithe.atlassian.net/browse/KAN-83) · [KAN-89](https://gametithe.atlassian.net/browse/KAN-89) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)
