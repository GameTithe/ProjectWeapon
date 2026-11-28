# KAN-117 계정 저장 버전과 충돌

[KAN-117](https://gametithe.atlassian.net/browse/KAN-117) · [KAN-53](https://gametithe.atlassian.net/browse/KAN-53) · [KAN-76](https://gametithe.atlassian.net/browse/KAN-76) · [KAN-82](https://gametithe.atlassian.net/browse/KAN-82)

앱의 계정 동기화 요청은 기준 개정, 안정적인 거래 ID, 거래 종류, 서버가 조회할 대상만 전달한다. 계정은 KAN-76의 유효한 로그인 세션에서 선택한다. 권위 호스트는 인증 컨텍스트에서 확인한 PlayerID로 Protected 스냅샷을 읽고, 거래 규칙에 소유권과 대상 검증을 맡긴다. 클라이언트가 저장 전체나 경제 변경량을 보내는 요청은 없다. 게스트 프로필과 현재 온라인 계정의 화면 캐시는 분리한다.

| 상황 | 결과 |
| --- | --- |
| 현재 개정에서 유효한 새 요청 | 무기·확정 코인·도감·기존 기록과 새 영수증을 같은 다음 개정으로 확정 |
| 두 요청이 같은 개정을 경쟁 | 저장소 write lock으로 하나만 확정, 다른 요청은 최신 개정과 상태 수신 |
| 같은 ID·종류·대상이 이미 확정 | 요청의 기준 개정이 오래되어도 원래 영수증과 최신 상태 반환 |
| 같은 ID를 다른 종류나 대상에 사용 | 요청 불일치 |
| 저장이 이뤄졌으나 응답이 유실 | 같은 ID로 재조회 또는 재시도, 동일 영수증 반환 |
| 로그인 유효 상태에서 연결 중단 | 동일 세션의 마지막 읽기 상태 표시, 출격·정산·뽑기·판매·보상 요청 거부 |
| 로그아웃·다른 로그인·세션 만료 | 이전 세션의 응답과 캐시 폐기 |

## 호스트 계약

- `AccountSaveAuthority`는 신뢰된 호스트에서 요청당 `IAccountSaveIdentity`를 받는다. `IAccountSaveOperations`는 부작용 없이 현재 개정에 대한 거래 결과를 계산한다. 영속 효과는 뒤이은 한 번의 비교 후 교체에서만 발생한다. 거래 규칙은 `SaveSnapshot.Next`를 사용해 읽은 기존 영수증 객체를 그대로 전달한다. 새 계정 동기화 영수증도 같은 값에 추가한다.
- `IProtectedAccountSaveStore` 구현은 같은 환경·PlayerID의 한 Protected 키를 사용한다. 기존 값에서 함께 읽은 공백 아닌 write lock을 모든 교체 요청에 포함한다. 잠금 충돌 때 권위 서비스는 최신 값을 한 번 읽고 이미 확정된 동일 ID가 있으면 재생한다. 다른 거래가 확정된 경우에는 기준 개정을 자동으로 변경하지 않는다. 초기 키 준비는 별도 서버 작업이다. 키나 잠금이 없으면 기존 값을 생성하거나 덮어쓰는 경로가 없다.
- `IAccountSaveEndpoint` 구현은 받은 KAN-76 세션에 해당하는 인증 자격과 취소 토큰으로 호스트를 호출한다. KAN-119가 재시도·조회 흐름을 연결할 때는 원 요청 ID를 보존한다. 클라이언트 응답에는 서버용 write lock을 포함하지 않는다. [KAN-119](https://gametithe.atlassian.net/browse/KAN-119)

Jira 범위는 동기화 서비스와 데이터 계약이다. 실제 Cloud Code 모듈과 Protected API 저장소 어댑터, 원격 프로비저닝과 계정 연결은 호스트 연동에서 검증한다. Unity 테스트는 KAN-76 세션으로 진입하고 테스트 저장소 어댑터에서 동일 개정의 두 요청을 제어해 write lock 경쟁을 재현한다. [Cloud Code server authority](https://docs.unity.com/en-us/cloud-code/server-authority) · [Cloud Save Player Data](https://docs.unity.com/en-us/cloud-save/concepts/player-data) · [Cloud Save write locks](https://docs.unity.com/en-us/cloud-save/concepts/write-locks) · [Cloud Save for Cloud Code](https://docs.unity.com/en-us/cloud-save/tutorials/cloud-code) · [Authentication sessions](https://docs.unity.com/en-us/authentication/session-management)

## 검증

동일 개정 경쟁은 제어된 두 호출이 같은 잠금을 읽고 한 호출이 먼저 교체하도록 재현했다. 응답 유실 사례는 별도의 파일 기반 테스트 저장소에 디스크 flush와 파일 교체로 기록하고, 새 어댑터와 새 세션에서 다시 불러왔다. 추가 거래를 확정한 뒤 원 요청을 세 차례 재시도해 기존 실물 ID·영수증과 최신 개정, 코인 및 이전 정산 기록을 확인했다. 늦게 도착한 이전 개정 응답도 같은 세션의 최신 캐시를 되돌리지 않는다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AccountSync.Tests --output "$env:TEMP/KAN-117-account-sync.xml" --timeout 180
```
