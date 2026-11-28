# KAN-107 LevelPlay 보상형 광고 테스트 연결

테스트 전용 Android·iOS 개발 빌드에서만 Unity Ads Mediation SDK를 시작한다. Unity Package Manager가 `com.unity.services.levelplay` 9.6.0을 공식 레지스트리에서 설치하고 잠금 파일을 생성했다. 코드에는 앱 키, 광고 단위 ID, 콜백 키가 없다. 전용 테스트 앱·테스트 광고 단위는 실행 환경에서 구성하고 운영 설정과 분리한다. [KAN-107](https://gametithe.atlassian.net/browse/KAN-107) · [KAN-55](https://gametithe.atlassian.net/browse/KAN-55) · [Unity LevelPlay 패키지](https://docs.unity.com/en-us/grow/levelplay/sdk/unity/package-integration)

## 앱에서 연결

1. 전용 테스트 기기와 광고 단위를 LevelPlay 테스트 설정에서 준비한다. Unity의 Android 또는 iOS 지원과 네이티브 의존성을 해결한 뒤 개발 빌드를 실행한다. 테스트 도중에는 같은 PlayerID로 로그인 상태를 유지한다. [LevelPlay 테스트 기기](https://docs.unity.com/en-us/grow/levelplay/platform/get-started/integration-testing)
2. KAN-76의 현재 세션과 테스트 앱 키를 `LevelPlayTestRuntime.TryInitialize`에 전달한다. SDK 초기화가 끝나면 테스트 광고 단위 및 인증된 테스트 권위 구현으로 `CreateAdapter`를 호출한다. SDK의 서명 UserID는 초기화한 계정에 고정된다. 다른 PlayerID의 테스트는 앱을 새로 시작한다.
3. `TryBegin(requestId)`로 로드한다. `Loaded`에서 `TryShowAsync(requestId)`를 호출한다. `ILevelPlayTestAuthority.AuthorizeAsync`가 현재 계정과 요청에 대해 발급한 일회용 토큰과 남은 표시 허가 시간으로 시작한다. 앱은 요청 왕복 동안의 단조 증가 경과를 보수적으로 차감해 만료된 표시를 차단한다. 예약 토큰은 LevelPlay의 Dynamic UserID로만 전달한다. [KAN-105](https://gametithe.atlassian.net/browse/KAN-105) · [LevelPlay 보상형 광고와 Dynamic UserID](https://docs.unity.com/en-us/grow/levelplay/sdk/unity/rewarded-ad-integration-package)
4. 로드 실패, 표시 전 취소, 표시 실패는 기존 `IRewardedAdAdapter` 결과로 전달한다. 광고가 닫히면 `AwaitingVerification`에서 같은 계정·요청·예약의 상태를 읽는다. 증빙이 아직 오지 않았으면 같은 ID로 다시 확인한다. KAN-97의 확정 영수증만 성공으로 표시한다. 증빙 거부는 `ClosedWithoutConfirmation`이며 SDK 자체의 완료 콜백만으로는 지급하지 않는다. 영수증을 기다리는 요청과 새 광고 표시는 분리된다. 로그아웃과 세션 만료에서는 어댑터가 폐기되고 늦은 응답은 화면으로 전달되지 않는다. 테스트 화면의 Unity 업데이트에서 `RecoverTimedOutPresentation`을 실행한다. 콜백이 없는 로드는 45초에 실패로 처리하고, 표시가 240초 동안 닫히지 않으면 운용자가 권위 영수증을 확인할 수 있도록 대기 상태로 넘긴다. [KAN-67](https://gametithe.atlassian.net/browse/KAN-67) · [KAN-97](https://gametithe.atlassian.net/browse/KAN-97)

## 권위 테스트 환경

- `LevelPlayTestProofVerifier`는 Unity Editor 검증 전용 어셈블리다. 실제 테스트 호스트에 이 검증기 형태를 연결할 때는 HTTPS로 LevelPlay 콜백을 받고, 전용 테스트 엔드포인트에서 `timestamp`, `eventId`, `userId`, `rewards`, `dynamicUserId`, `appKey`, `signature`로 이름을 정규화한다. 중복 필드는 거부한다. 키는 서버 설정에서 읽고 앱에 배포하지 않는다.
- 전용 앱 ID, 설정 보상량, 공급자 문서의 S2S 서명 및 예약의 소유 계정을 확인한다. Dynamic UserID와 앱 키는 공급자 서명에 포함되지 않는다. 전용 테스트 앱은 별도의 콜백 키를 사용한다. 신뢰된 수신 호스트는 서명된 이벤트·시각·계정이 같은 표시의 예약에 대응함을 확인해 검증기에 전달한다. KAN-97 서비스가 해당 계정과 예약을 다시 확인하고 단일 영수증·지급·횟수를 저장한다. 응답 확인은 저장 또는 동일 영수증이 확정된 뒤에만 호스트에서 반환한다. [LevelPlay S2S 콜백](https://docs.unity.com/en-us/grow/levelplay/platform/settings/server-to-server-callback)
- 실제 호스트 어댑터는 해당 계정과 요청의 영수증을 `ILevelPlayTestAuthority.ReadAsync`로 읽는다. 원격 데이터 구성과 HTTP 호스트는 현재 프로젝트에 연결돼 있지 않다.

## 확인 범위

```powershell
unity test . --mode EditMode --filter ProjectWeapon.LevelPlayTest.Tests --output "$env:TEMP/KAN-107-levelplay.xml"
```

실제 관리 SDK의 `ILevelPlayRewardedAd`를 쓰는 테스트에서 로드 실패, 사전 취소, 표시 오류, 순서가 바뀐 로컬 콜백, 같은 요청 영수증, 늦은 허가의 만료, 예약 재사용 차단, 세션 종료와 표시 종료 뒤의 독립 요청를 검증한다. S2S 테스트는 전용 가상 키로 변조된 서명·예약·앱·중복 필드에 대한 거부와 KAN-97의 단일 지급을 검증한다. Android/iOS 네이티브 광고 제공과 테스트 호스트는 별도 실기기 검증이 필요하다. 관리 패키지 설치 중 SDK가 별도 Android/iOS 의존성 파일을 생성했다. 실제 기기 의존성 조합을 확인한 뒤 그 플랫폼의 설정을 확정해야 한다. 현재 Windows 환경의 Android 빌드 지원 설치는 UAC에서 중단됐고 실제 테스트 앱과 광고 단위 및 콜백 호스트는 설정돼 있지 않다. [KAN-42](https://gametithe.atlassian.net/browse/KAN-42) · [KAN-43](https://gametithe.atlassian.net/browse/KAN-43)

실제 모바일 호스트는 KAN-105에서 요구하는 확인된 최대 영상 표시 시간을 확보한 뒤, 현재 표시 임대 안에 영상이 모두 들어가는 최종 시작 한계로 `Remaining`을 계산해야 한다. 앱의 240초 복구 경로는 SDK 콜백에 응답하기 위한 시간이며, 네이티브 영상이 실제로 종료되는 시각을 증명하지 않는다. 실기기에서 이 경계를 확인하기 전에는 테스트 광고의 활성화를 보류한다.
