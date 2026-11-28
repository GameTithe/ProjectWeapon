# KAN-43 iOS 개발 빌드 조건과 검증 경로

검증 기준은 `main`의 `6efb40a`, Unity 6000.6.9f1 및 KAN-17의 iOS 15·A13급 가로 양방향 조합이다. 이 체크아웃의 Player 설정은 iOS 최소 15.0이다. 실제 출력과 기기 설치의 차단 요건을 확인했다. [KAN-43](https://gametithe.atlassian.net/browse/KAN-43) · [KAN-17](https://gametithe.atlassian.net/browse/KAN-17)

| 항목 | 확인 결과 | 필요한 환경 |
| --- | --- | --- |
| 호스트 | 현재 Windows 11 Home이다. 셸에서 `xcodebuild`를 찾을 수 없었다. | 접근 가능한 macOS에서 Xcode를 실행할 수 있어야 한다. Unity 문서는 6.6에 Xcode 16 이상을 권장한다. [Unity iOS 요구사항](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/iphone/ios-introducing/ios-requirements-and-compatibility) |
| Unity 모듈 | 현행 에디터의 PlaybackEngines에는 WebGL 및 Windows 지원만 있다. | macOS의 Unity 6000.6.9f1에 iOS Build Support를 추가한다. [Unity iOS 앱 빌드](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/iphone/ios-building-and-delivering/build-process) |
| 빌드 경로 | 저장소의 GitHub Actions 워크플로는 PR의 Windows Player를 빌드한다. 등록된 iOS 프로필과 macOS CI 경로는 확인되지 않았다. | 승인된 macOS 호스트에서 Debug 또는 Development Build로 로컬 검증을 시작한다. [현재 워크플로](https://github.com/GameTithe/ProjectWeapon/blob/6efb40a/.github/workflows/unity-pr-build.yml) |
| 기기 서명 | 프로젝트의 `appleDeveloperTeamID`가 비어 있다. | 실기기 실행에는 관리자가 사용을 허용한 Apple 계정 또는 팀, 고유 Bundle Identifier, 개발 서명 및 테스트 기기 등록이 필요하다. [Apple 실기기 서명](https://developer.apple.com/documentation/xcode/running-your-app-on-simulated-or-physical-devices) |
| 테스트 기기 | Windows의 연결 장치에서 iPhone·iPad를 확인하지 못했다. | A13급의 iOS 15 실기기와 보유 최신 iOS 기기에서 버전을 기록하고 실행한다. |

## macOS 검증 절차

1. 위 커밋 또는 검증할 `main`을 열어 `xcodebuild -version`과 Unity 버전, Build Profile의 iOS 모듈을 확인한다. Profile에 목적 씬을 지정하고 Deployment Target을 iOS 15로 유지한다.
2. Simulator SDK와 Development Build로 임시 폴더에 새 Xcode 프로젝트를 출력한다. Xcode의 `Unity-iPhone` 스킴과 설치된 iPhone 시뮬레이터를 선택해 실행하고 앱 부팅, 가로 양방향 레이아웃과 재실행을 확인한다. 기기 성능은 실기기 단계에서 측정한다. [Unity 빌드 절차](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/iphone/ios-building-and-delivering/build-process)
3. 실제 기기는 별도 폴더에 Device SDK로 출력하고 승인된 팀·Bundle ID로 서명한다. Xcode 장치 목록에서 대상 모델과 OS를 확인하고 실행한다. 최초 로드, 홈 전환·복귀, 종료·재실행, 안전 영역·회전, 터치가 겹치는 조작을 기록한다. [Apple 실행 절차](https://developer.apple.com/documentation/xcode/running-your-app-on-simulated-or-physical-devices) · [KAN-39](https://gametithe.atlassian.net/browse/KAN-39)
4. 시뮬레이터와 실기기 각각에 커밋, Xcode·Unity 버전, SDK·OS, 씬 경로, 빌드·설치·실행 결과를 남긴다. 부팅 검증에는 등록된 기본 씬을 쓰고, 탐험 루프는 통합 씬이 들어간 빌드에서 재현한다. [KAN-193](https://gametithe.atlassian.net/browse/KAN-193)

현재 세션에서 확인한 것은 Windows의 도구·Unity 모듈·저장소 설정과 공식 빌드 경로다. macOS/Xcode 및 개발 서명·기기 접근이 확보돼야 Xcode 프로젝트와 실기기 빌드를 검증할 수 있다.
