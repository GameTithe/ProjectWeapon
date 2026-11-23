# KAN-16 Unity 프로젝트 구조 조사

기준은 GitHub `main`의 커밋 `261a45a`와 현재 열려 있는 로컬 Unity 프로젝트다. 관련 작업은 [KAN-16](https://gametithe.atlassian.net/browse/KAN-16)이다.

## 현재 구조

| 항목 | 확인된 구성 |
| --- | --- |
| Unity | `ProjectSettings/ProjectVersion.txt`의 에디터 버전은 `6000.6.9f1`이다. |
| 렌더링 | URP `17.6.0`. 품질 설정의 렌더 파이프라인이 `Assets/Settings/UniversalRP.asset`을 참조하고, 기본 렌더러는 `Renderer2D.asset`이다. |
| 씬 | 빌드에 등록된 씬은 `Assets/Scenes/SampleScene.unity` 하나다. 메인 카메라와 전역 2D 조명이 있다. 별도 URP 2D 씬 템플릿은 `Assets/Settings/Scenes/URP2DSceneTemplate.unity`다. |
| Tilemap | `com.unity.2d.tilemap` `1.0.0`과 `com.unity.2d.tilemap.extras` `9.0.1`이 설치 대상이다. 추적 파일에 게임 구역용 Tilemap이나 타일 에셋은 없다. |
| 입력 | Input System `1.20.1`과 `Assets/Settings/InputSystem_Actions.inputactions`의 Player 및 UI 맵이 있다. Player 맵에는 `Move` 액션이 있다. |
| 테스트 | Unity Test Framework `1.8.0`이 설치 대상이다. 추적 파일에 게임용 EditMode 또는 PlayMode 테스트와 해당 어셈블리 정의는 없다. |
| 코드 | `Assets`에서 추적하는 C#과 asmdef는 `Assets/Welcome`의 Unity 템플릿 안내용이다. 런 상태, 전투, 인벤토리, 정산, 저장 구현은 새로 필요하다. |

## 첫 플레이 루프 변경 지도

| 단계 | 재사용할 구성 | 새로 필요한 부분 |
| --- | --- | --- |
| [KAN-18](https://gametithe.atlassian.net/browse/KAN-18) 최소 실행 씬 | URP 2D 렌더러 및 씬 템플릿 | 별도 테스트 씬, Grid와 Tilemap, 카메라·스프라이트 출력 및 Play Mode 확인 |
| [KAN-19](https://gametithe.atlassian.net/browse/KAN-19) 모듈·이벤트 계약 | 기존 프로젝트와 패키지 구성 | 런, 인벤토리, 도감, 경제, 저장, 광고, 랭킹의 책임과 단방향 의존성 및 이벤트 계약 |
| [KAN-34](https://gametithe.atlassian.net/browse/KAN-34) 테스트 실행 틀 | Unity Test Framework | 게임용 EditMode·PlayMode 테스트 어셈블리와 실행 절차 |
| [KAN-46](https://gametithe.atlassian.net/browse/KAN-46) 이동 입력 | 기존 Input System과 Player 액션 맵 | 가상 조이스틱 UI, 이동 입력 어댑터, 터치 소유권·종료 처리 검증 |

## 기준 차이와 검증 범위

- 로컬 `master`와 원격 `main`의 Unity 추적 파일을 비교했다. `Assets`와 `Packages`는 동일하며, `ProjectSettings`의 차이는 원격에서 비워 둔 Unity Cloud 연결 필드다. 로컬에는 작업 규칙 문서와 기획서가 별도로 있다.
- Git 추적 목록, 프로젝트 버전 파일, 패키지 매니페스트, 빌드 씬, 렌더러 참조 및 입력 액션을 읽어 대조했다. 로컬 에디터가 그 버전으로 `SampleScene`을 열고 있는 것을 확인했다.
- 앞선 GitHub 빌드는 원격 기준의 Windows Player 빌드 근거다. 이 문서 작업에서는 Play Mode, 신규 테스트, 재빌드, 모바일 실기기 검증을 수행하지 않았다. [기준 CI](https://github.com/GameTithe/ProjectWeapon/actions/runs/45019121124)
