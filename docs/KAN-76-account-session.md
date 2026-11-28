# KAN-76 게스트와 온라인 인증 세션

[Jira KAN-76](https://gametithe.atlassian.net/browse/KAN-76) · [KAN-53 계정·저장 정책](https://gametithe.atlassian.net/browse/KAN-53)

## 서비스 연결

- Unity 시작 흐름에서 `new AccountSessionBoundary(LocalGuestProfile.GetOrCreate(), new UnityPlayerAccountProvider())`로 단일 소유자를 만든다. 초기 모드는 로컬 게스트다. `GuestProfileId`는 설치에서 만든 로컬 저장 선택용 ID이며 Unity 인증이나 네트워크 없이 사용할 수 있다.
- 정리된 로비에서 사용자가 온라인 로그인을 선택하면 `SignInAsync()`를 호출한다. Project ID가 구성된 경우에만 SDK를 초기화하고 Player Accounts 브라우저 결과를 Unity Authentication으로 교환한다. SDK가 현재 요청을 승인하고 반환한 UGS PlayerId만 온라인 세션에 들어간다.
- 경제·광고·랭킹 어댑터는 `TryGetOnlineSession` 또는 `RunOnlineAsync`에서 받은 세션만 사용한다. `OnlineAccountSession.Cancellation`을 하위 작업에 전달한다. 요청이 전송된 경우에도 응답 적용 직전에 `IsCurrent`를 다시 확인한다. 서버는 요청의 인증 컨텍스트에서 플레이어를 식별한다.
- `UseGuestAsync()`는 즉시 게임의 온라인 세션과 대기 결과 적용을 차단하고, 이어서 두 SDK의 로그인을 정리한다. 동일 PlayerId로 다시 로그인해도 이전 요청을 무효화한다. 만료나 SDK PlayerId 변경도 동일한 세션 경계를 적용한다. 소유자를 내릴 때 `CloseAsync()`로 이벤트 연결을 해제한다.
- 게스트 저장과 온라인 요청의 생명주기는 분리한다. 연결이 없을 때의 동일 계정 읽기 전용 스냅샷, 서버 쓰기, 온라인 런 복구와 실제 UI 전환은 상위 연동에서 다룬다. Unity 호출과 경계는 같은 게임 스레드에서 순서대로 사용한다.

## SDK와 로그

직접 의존성은 Unity 공식 레지스트리의 Authentication 3.9.0이다. Authentication과 Player Accounts는 이 패키지의 두 런타임 어셈블리다. Services Core 1.19.0은 UPM이 내부 의존성으로 받는다. [Authentication 레지스트리](https://packages.unity.com/com.unity.services.authentication) · [Unity Player Accounts](https://docs.unity.com/en-us/authentication/unity-player-accounts)

토큰은 두 SDK 사이의 교환에서만 읽는다. 게임 호출자는 자격 증명이나 SDK 예외 본문 대신 상태 코드를 받는다. SDK 3.9.0의 기본 로거는 OAuth 요청 URL 및 인증 예외를 인증 태그와 함께 전달한다. Project ID를 확인한 뒤 SDK 초기화 전에 설치되는 인증 경계 필터가 두 인증 태그와 SDK 인증 예외만 제외한다. 기존 Unity 로그 처리기에 다른 로그를 그대로 전달하며, 폐기된 SDK 콜백도 처리할 수 있도록 필터는 앱 생명주기 동안 유지한다.

## 검증

Unity Test Runner의 EditMode에서 `ProjectWeapon.Accounts.Tests`를 실행한다. 계정 전환·만료·로그아웃, 늦은 SDK 응답과 같은 계정 재로그인, 게스트 ID 보존, 실패·재시도, Unity 로거 전달, 공식 브라우저 API 이벤트를 사용하는 어댑터를 검증한다. 실제 프로젝트가 연결되지 않은 상태에서 공식 어댑터의 미구성 반환도 실행한다.

실계정 로그인은 구성된 Unity 프로젝트와 Player Accounts Client ID가 필요하다. 이번 변경은 로컬 SDK 패키지와 연결 코드만 검증했다. 원격 프로젝트 연결, 인증 공급자 설정, 실제 계정 로그인과 배포는 실행하지 않았다. [Unity Authentication 작동 방식](https://docs.unity.com/en-us/authentication/how-authentication-works) · [Unity Authentication 세션과 캐시](https://docs.unity.com/en-us/authentication/session-management)
