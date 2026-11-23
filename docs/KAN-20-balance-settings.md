# KAN-20 공통 밸런스 설정

공통 설정의 버전, 승인 표기와 입력 유효성을 다룬다. 실제 수치는 각 도메인의 에셋에서 정한다. 기존 구역 에셋은 해당 도메인에 그대로 둔다. [KAN-20](https://gametithe.atlassian.net/browse/KAN-20) · [KAN-88](https://gametithe.atlassian.net/browse/KAN-88)

## 에셋 작성

Unity의 `Assets > Create > ProjectWeapon > Balance Settings`에서 에셋을 만든다. 새 에셋의 범위와 가중치 목록은 비어 있다.

- `Schema Version`은 데이터 구조를 식별한다. 이 구현은 1을 읽으며, 미지원 구조를 만나면 예외를 반환한다.
- `Revision`은 같은 구조에서 조정된 설정 묶음을 구분하는 양의 정수다. 팀에서 비교할 설정을 저장할 때 갱신한다.
- 각 설정의 `Key`는 묶음 내의 고정 식별자다. 범위 설정과 가중치 설정을 합쳐 중복을 허용하지 않는다. 원소의 `Approval`은 기본값이 `Draft`이며 승인된 수치를 기록할 때 `Approved`로 표시한다.

| 범위 종류 | 단위와 확인 기준 |
| --- | --- |
| `TimeLimitSeconds` | 양의 정수 초 |
| `DamagePerSecond` | 0 이상의 초당 피해. 소수 허용 |
| `CoinSupply` | 0 이상의 정수 코인 |
| `BagCapacity` | 0 이상의 정수 슬롯 |

범위는 두 끝값을 포함하며 최소값은 최대값 이하다. 가중치 분포는 원소별 고유 ID와 0–10,000bp를 입력한다. 1%는 100bp이며 합계는 10,000bp여야 한다. 읽을 때 `GetRanges()`와 `GetDistributions()`가 구성 전체를 검증하고 읽기 전용 목록을 반환한다. `Validate()`는 목록을 열지 않고 유효성만 확인한다.

## 테스트 재현

저장소 루트에서 실행한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Balance.Tests.BalanceSettingsTests --output "$env:TEMP/KAN-20-results.xml"
```

빈 설정 틀, 버전과 승인 표기, 유효한 단일값 및 0·10,000bp를 확인한다. 뒤집힌 범위, NaN·무한대, 잘못된 단위의 값, 빈 값·중복 ID 및 9,999·10,001bp 합계는 실패를 재현한다. 테스트에서 쓰는 수치는 유효성 검사용 입력이다.
