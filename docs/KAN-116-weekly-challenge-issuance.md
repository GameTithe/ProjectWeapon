# KAN-116 주간 도전 조건 발급

`Server/WeeklyChallenge`의 독립 .NET 10 Cloud Code 모듈은 서버가 주간 도전 조건과 시도 영수증을 발급하는 경로다. `IssueWeeklyAttempt`에 클라이언트가 보내는 값은 재시도에 재사용할 UUID `requestId` 한 개다. 계정은 Cloud Code의 `IExecutionContext.PlayerId`로 정하고 프로젝트·환경도 같은 컨텍스트에서 선택한다. 초기 계정 설정과 상태 조회는 인증된 온라인 계정의 경계를 따른다. [KAN-116](https://gametithe.atlassian.net/browse/KAN-116) · [KAN-53](https://gametithe.atlassian.net/browse/KAN-53) · [KAN-76](https://gametithe.atlassian.net/browse/KAN-76) · [Cloud Code 모듈 구조](https://docs.unity.com/en-us/cloud-code/modules/how-to-guides/module-structure)

## 발급과 재시도

- 한 회차는 서버 시각 월요일 00:00 UTC부터 다음 월요일 직전까지다. 조건은 회차 시작, 조건 버전, 맵 버전·시드, 적·상자·규칙·임시 장비 버전, 임시 무기 정의 ID, 제한시간을 묶는다. 같은 환경·회차의 모든 계정은 최초로 고정된 데이터 전체를 받는다. 이후 후보가 변경돼도 이미 고정된 조건을 읽는다.
- 승인된 발급마다 서버가 고유 시도 ID를 만든다. 동일 계정의 회차별 한도는 확정 발급 5회다. 같은 계정의 같은 요청 ID는 발급 영수증을 먼저 조회한다. 새 프로세스, 응답 유실, 저장 경합, 회차 변경 뒤에도 원래 시도 ID와 조건, 발급·만료 시간이 유지된다. 다른 계정은 같은 요청 ID를 자기 계정 범위에서 사용할 수 있다.
- 만료는 발급 시각에 제한시간을 더한 시각과 다음 월요일 중 이른 값이다. 만료 시각과 정확히 같을 때부터 `Expired`로 원래 영수증을 반환한다. 월요일 경계를 넘긴 뒤 새로운 요청은 새 회차를 사용한다. 한도 도달이나 저장 장애로 발급된 시도가 없으면 그 요청에 영수증이 없으므로 같은 ID를 새 회차에서 사용할 수 있다. [KAN-61](https://gametithe.atlassian.net/browse/KAN-61)
- 성공 전후 연결이 불확실하면 `requestId`를 그대로 재사용한다. 새 ID는 사용자가 새 시도를 시작할 때만 생성한다. 발급 영수증의 영속 기록을 유지해야 이전 회차 요청을 원래 시도로 복원할 수 있다.

| 결과 | 응답 |
| --- | --- |
| `Available` | 사용 가능한 발급 영수증. 최초 확정과 기존 활성 시도 조회에 동일한 응답을 반환한다. |
| `Expired` | 만료된 원래 영수증. 해당 시도를 다시 활성화하지 않는다. |
| `Unauthorized`, `InvalidRequest` | 인증된 플레이어 또는 유효한 요청 UUID가 없다. |
| `WeeklyLimitReached` | 해당 회차의 확정 발급이 5회다. |
| `AuthorityUnavailable` | 저장 원장·잠금·조건을 안전하게 사용할 수 없다. 같은 요청 ID로 조회할 수 있다. |
| `Retryable` | 한도 내 재시도로 쓰기 경합을 끝내지 못했거나 저장 응답을 확인할 수 없다. 같은 요청 ID로 다시 조회한다. |

[KAN-126](https://gametithe.atlassian.net/browse/KAN-126)의 서버 검증은 `WeeklyChallengeIssuer.FindIssuedAttemptAsync`에 인증 컨텍스트의 소유자 ID와 검증 중인 시도 ID를 전달한다. 해당 계정의 확정 기록만 조회하며, 만료된 기록도 제출의 접수·완료 시각을 판정할 수 있도록 돌려준다. 공개 모듈 함수는 발급 한 개다. 점수 판정·제출 중복·순위는 후속 이슈에서 연결한다.

## 서버 저장 준비

- 환경별 접두어는 `weekly_challenge_v1_<EnvironmentId를 N 형식으로 표현한 UUID>`다. 플레이어 Protected 데이터에서 이 이름의 항목이 필요하다. Cloud Save 값은 JSON 문자열이며 초기 구조는 `{"AccountId":"<인증된 PlayerId>","Attempts":[]}`다. 승인된 온라인 계정 준비 단계에서 발급을 열기 전에 해당 레코드를 준비한다. 첫 초기화는 외부 서버에서 단일 초기화 담당자 또는 원자적인 생성 보장을 사용하고, 기존 원장은 다시 초기화하거나 제거하지 않는다.
- 같은 접두어의 Private Custom ID에 UTC 월요일 `yyyy_MM_dd` 항목이 필요하다. 값은 `Candidate`(C# `WeeklyConditions`와 같은 JSON 속성) 및 초기 `Frozen: null`을 가진 JSON 문자열이다. 일정 관리 경로는 활성화 전에 준비하고, 발급 모듈이 고정한 `Frozen`은 그대로 보존한다. 해당 회차의 콘텐츠를 바꿀 때 다음 회차 설정을 추가한다.
- 발급 모듈은 두 항목 모두 서버에서 읽은 기존 버전의 비어 있지 않은 write lock으로 대체한다. 새 값이 없는 경우 임의의 레코드를 만들지 않는다. 조건 고정의 잠금이 충돌하면 다시 읽어 승자의 확정값을 사용한다. 원장 교체에서 시도 번호·동일 요청 영수증·조건을 한 번에 확정하고, 충돌 시 최신 원장에서 다시 판정한다. 테스트는 409 충돌과 확정 후 503 응답을 재현한다. [Cloud Save 쓰기 잠금](https://docs.unity.com/en-us/cloud-save/concepts/write-locks) · [Cloud Code에서 Cloud Save 사용](https://docs.unity.com/en-us/cloud-save/tutorials/cloud-code)
- 회차를 지나도 질의 가능한 요청 ID와 시도 기록을 유지한다. Protected 저장 한도에 대비해 원장을 이전할 경우 기존 요청과 시도 ID의 조회를 같은 권위 경계에서 보존해야 한다.

## 로컬 재현

Unity 6000.6.9f1 프로젝트 바깥에서 .NET 10 SDK로 빌드한다. Cloud Code 3.x의 Unity Editor 내장 작성 흐름은 Unity 6.7 이상이 필요하다. 이 모듈의 Linux x64 출력은 독립 작성·패키징 경로를 따른다. [Cloud Code 시작하기](https://docs.unity.com/en-us/cloud-code/modules/getting-started) · [Cloud Code 패키징](https://docs.unity.com/en-us/cloud-code/modules/how-to-guides/package-code)

```powershell
dotnet restore Server/WeeklyChallenge.Tests/WeeklyChallenge.Tests.csproj --locked-mode
dotnet test Server/WeeklyChallenge.Tests/WeeklyChallenge.Tests.csproj -c Release --no-restore
dotnet publish Server/WeeklyChallenge/WeeklyChallenge.csproj -c Release -r linux-x64 --no-self-contained -p:PublishReadyToRun=true
```

.NET 10.0.401에서 Release 테스트 22/22건이 통과했다. 실제 Cloud Code SDK 타입을 통한 모듈 바인딩은 로컬 대역으로 검증했다. 같은 회차 조건과 고정 경합, 다섯 번째 발급 경합, 동일 요청 경합, 응답 유실과 회차 경계, 소유자 불일치, 저장 잠금·설정 누락, 손상된 레코드를 다룬다. 파싱 가능한 저장이라도 표현 가능한 날짜 범위를 벗어나거나 같은 회차의 확정 조건이 달라지면 `AuthorityUnavailable`로 안전하게 중단한다. Linux x64 게시 폴더의 모듈 DLL, 의존성 DLL 및 `.deps.json`을 확인했다. 실제 서비스 프로비저닝·배포·서비스 간 연결 검증이 남아 있다.
