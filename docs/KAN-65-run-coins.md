# KAN-65 탐험 코인

확정 코인은 `WalletBalance`의 불변 값이다. 탐험마다 `RunCoinAccumulator`를 실제 `RunId`와 함께 만든다. 수급을 등록하면 그 런의 임시 코인만 증가한다. [KAN-65](https://gametithe.atlassian.net/browse/KAN-65) · [KAN-19](KAN-19-domain-contracts.md)

## 보상 범위

`Assets/ProjectWeapon/Wallet/PrototypeRunCoinRewards.asset`은 KAN-20의 `BalanceSettings` 포맷으로 만든 편집 가능한 데이터다. 아래 값은 전부 가안(`Draft`)이며 양쪽 끝값을 포함한다. 출처는 기획서 v3.0 6쪽 §7이다. [KAN-20](KAN-20-balance-settings.md)

| 획득처 | 가안 코인 |
| --- | --- |
| 일반 몬스터 | 2–5 |
| 정예 몬스터 | 15–30 |
| 상자 | 20–50 |
| 보스 | 100–200 |

`RunCoinRewardTable.Roll(source, sampleInclusive)`에 최소·최대 양쪽을 포함하는 추첨 함수를 전달한다. 범위를 벗어난 결과는 예외를 일으킨다. `AddReward(amount)`는 검증된 지급액을 해당 런에 누적한다. 실제 적·상자에서 지급을 호출하는 연결은 대상 기능이 다룬다.

## 저장 경계

최종 `RunResult`와 현재 확정 잔고를 `ProposeSettlement()`에 전달한다. 성공은 임시 코인 전액을 더하는 후보를 만들고, 실패는 확정 잔고를 그대로 유지하며 임시 코인 전액을 폐기하는 후보를 만든다. 다른 런의 종료 결과는 거절한다. 후보 계산이나 재시도로 확정·임시 잔고를 바꾸지 않는다.

정산 담당은 후보 잔고가 포함된 스냅샷의 저장 성공을 확인한 뒤 새 확정 잔고를 게시하고 `AcknowledgeCommitted()`를 호출한다. 성공과 실패 모두 그때 해당 런의 임시 코인을 지우고 누적을 닫는다. 저장 실패 동안에는 임시 코인이 남아 재시도할 수 있다. 정산 키 중복 방지와 영구 저장은 별도 계약이다. [KAN-73](KAN-73-atomic-snapshot.md) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

## 테스트 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Wallet.Tests --output "$env:TEMP/KAN-65-results.xml"
```

직렬화 에셋의 네 범위와 경계값, 오류 입력, 런 분리, 귀환 성공·사망·시간초과, 저장 확인 전 재시도와 수치 오버플로를 검증한다.
