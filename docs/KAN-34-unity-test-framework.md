# Unity 테스트 실행 틀

EditMode는 프레임 없이 검사할 수 있는 상태와 계산을 다룬다. PlayMode는 프레임 진행, 씬, 입력 같은 Unity 실행 환경이 필요한 조합을 다룬다. 실행 틀은 게임 도메인 구현과 별도로 유지한다. [KAN-34](https://gametithe.atlassian.net/browse/KAN-34) · [KAN-19 공통 계약](KAN-19-domain-contracts.md)

| 폴더 | 어셈블리 | 샘플 |
| --- | --- | --- |
| `Assets/Tests/Framework/EditMode` | `ProjectWeapon.Tests.Framework.EditMode` | 고정 시각에서 시작해 주입한 시간만큼 전진하는지 검증 |
| `Assets/Tests/Framework/PlayMode` | `ProjectWeapon.Tests.Framework.PlayMode` | Unity 프레임이 진행돼도 주입한 시계는 직접 전진시킨 만큼만 변하는지 검증 |
| `Assets/Tests/Framework/Support` | `ProjectWeapon.Tests.Framework.Support` | 테스트용 `ManualTestClock`과 실패 검사용 환경 변수 계약 |

샘플은 시스템 시간 대신 `TimeSpan` 입력을 받는 `ManualTestClock`을 사용한다. 게임 모듈 테스트에서 시간, 저장소, ID 발급기, 후보 목록에 테스트별 고정 값을 주입할 수 있다. 시간 샘플은 주입 방법을 보여 준다.

## 정상 실행

측정하려는 프로젝트 폴더를 직접 지정한다. 아래 PowerShell 예시는 현재 폴더가 해당 Unity 프로젝트 루트일 때 실행한다. 같은 폴더에서 Unity 에디터가 열려 있으면 에디터의 Test Runner에서 모드를 선택하거나 별도 작업 폴더에서 CLI를 실행한다.

```powershell
$reports = Join-Path $env:TEMP ("ProjectWeapon-Test-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $reports | Out-Null
$env:PROJECTWEAPON_RUN_FAILURE_PROBE = $null

unity test . --mode EditMode --output "$reports/editmode.xml" --timeout 900
unity test . --mode PlayMode --output "$reports/playmode.xml" --timeout 900
```

각 명령은 종료 코드 0을 반환해야 한다. 결과는 `$reports`의 NUnit XML에 남는다. 기본 샘플의 기대 결과는 EditMode 통과 3건, PlayMode 통과 2건이다. 각 모드의 실패 탐지는 환경 변수로 요청할 때만 해당 테스트를 실패시킨다. 이후의 도메인 테스트는 해당 모드에 추가로 집계된다.

## 실패 검출 확인

필터는 모드별 의도 실패 샘플 한 건으로 제한한다. 앞서 생성한 결과 폴더를 사용한다.

```powershell
$previous = $env:PROJECTWEAPON_RUN_FAILURE_PROBE
try {
    $env:PROJECTWEAPON_RUN_FAILURE_PROBE = "1"
    unity test . --mode EditMode --filter ProjectWeapon.Tests.Framework.EditMode.FrameworkEditModeTests.IntentionalFailureProbe --output "$reports/editmode-failure.xml" --timeout 900
    $editExit = $LASTEXITCODE
    unity test . --mode PlayMode --filter ProjectWeapon.Tests.Framework.PlayMode.FrameworkPlayModeTests.IntentionalFailureProbe --output "$reports/playmode-failure.xml" --timeout 900
    $playExit = $LASTEXITCODE
} finally {
    $env:PROJECTWEAPON_RUN_FAILURE_PROBE = $previous
}

if ($editExit -ne 8 -or $playExit -ne 8) {
    throw "Expected the test runner to report one intentional failure in each mode."
}
```

CLI 종료 코드 8과 XML의 실패 1건 및 `KAN-34 intentional failure probe` 메시지를 함께 확인한다. 결과 XML과 로그는 작업 폴더 밖에 둔다.


## 이번 작업의 검증 결과

Unity 6000.6.9f1과 Unity CLI 1.0.1로 2026-11-23에 각 모드를 별도 배치 실행했다.

| 결과 파일 | NUnit 결과 | CLI 종료 코드 |
| --- | --- | --- |
| `editmode.xml` | 3건 통과, 실패 0건 | 0 |
| `playmode.xml` | 2건 통과, 실패 0건 | 0 |
| `editmode-failure.xml` | 실패 탐지 샘플 1건에서 의도한 메시지를 검출 | 8 |
| `playmode-failure.xml` | 실패 탐지 샘플 1건에서 의도한 메시지를 검출 | 8 |

XML과 CLI 로그는 로컬 `%TEMP%/ProjectWeapon-KAN34-20261123-verified-1795428364`에 남겼다.
