# KAN-37 강제 종료와 저장 재접속 검증

[KAN-47 승인안](https://gametithe.atlassian.net/browse/KAN-47)은 중단 뒤에도 최초 탐험 기한을 유지한다. 체크포인트와 연속 시간을 검증할 수 없는 새 실행은 예약 자산을 보존하고 복구 대기로 남는다. [KAN-189](https://gametithe.atlassian.net/browse/KAN-189)의 재접속에서 확인된 확정 영수증이 있으면 같은 결과를 돌려준다. [KAN-37](https://gametithe.atlassian.net/browse/KAN-37) · [KAN-94](https://gametithe.atlassian.net/browse/KAN-94) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

| 장애 주입 지점 | 동일 경로를 다시 열었을 때 | 승인된 같은 결과의 재요청 |
| --- | --- | --- |
| 임시 파일 쓰기 직전 | 리비전 1, 코인 100, 기존 반입 예약과 이력. 영수증 없이 복구 대기 | 정상 처리 한 번 뒤 같은 영수증 |
| 임시 파일 절반을 쓰고 플러시한 직후 | 리비전 1. 강제 종료로 남은 불완전 임시 파일은 복구 후보에서 제외 | 정상 처리 한 번 뒤 같은 영수증 |
| 임시 파일 전체 플러시 뒤 교체 직전 | 리비전 1. 강제 종료로 남은 완전한 임시 파일도 복구 후보에서 제외 | 정상 처리 한 번 뒤 같은 영수증 |
| 원자 교체 직후 응답 전 | 리비전 2와 확정 영수증. 백업에 리비전 1 원문 보존 | 이미 확정된 동일 영수증, 파일 변경 없음 |
| 정상 체크섬을 가진 미지원 포맷 | `UnsupportedVersion`과 버전 번호. 백업으로 내리지 않고 복구 대기 | 저장과 백업의 원본 바이트 보존 |
| 본 파일 체크섬 오류, 백업 유효 | `RecoveredFromBackup`과 본 파일 진단. 원본·백업을 그대로 두고 복구 대기 | 백업의 자산만 조회 |
| 본 파일과 백업 모두 손상 | `Corrupt`와 양쪽 진단, 복구 대기 | 저장과 백업의 원본 바이트 보존 |

정상 귀환 검증 fixture는 보유 코인 100, 탐험 코인 35, 최초 도감 5종 보상 500으로 최종 635다. 정산 한 건, 단계 보상 한 건, 동일 실물 ID로 자산을 비교한다. 사망 검증에서는 코인 100과 기존 미반입 무기·이력이 보존되며 반입·전리품은 사라진다. 장애별 마지막 저장은 같은 초기 프로필에서 정상 정산한 파일과 바이트 단위로 일치한다. 파일을 새 저장소 객체로 열고 세 차례 다시 정산하여 자산·영수증과 수정 여부를 검사한다.

## 재현

기본 EditMode 검증은 실제 `RunSettlementCoordinator`, `AtomicSnapshotStore`, `SnapshotCodec`, `RunRecoveryCoordinator` 경로를 사용한다. `SnapshotFileOperations`에 IO 예외를 넣어 정산의 정상 귀환과 사망을 각각 재현한다. 이 경로의 예외는 정리 구문을 실행한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.SaveCrashQA.Tests.ForcedExitSaveRecoveryTests --output "$env:TEMP/KAN-37-editmode.xml"
```

Windows의 Unity 6000.6.9f1과 기본 Windows PowerShell에서는 테스트 Editor 프로세스의 환경 변수로 자식 프로세스 종료 네 가지를 활성화한다. 작성기는 Editor 설치에 포함된 .NET Standard 2.1 호환 어셈블리를 함께 읽는다. 이미 열린 Editor에는 환경 변수가 전파되지 않으므로 배치 실행을 사용한다.

```powershell
$env:PROJECTWEAPON_KAN37_CHILD = '1'
try {
    unity test . --mode EditMode --filter ProjectWeapon.SaveCrashQA.Tests.ForcedExitSaveRecoveryTests --output "$env:TEMP/KAN-37-windows.xml"
} finally {
    Remove-Item Env:PROJECTWEAPON_KAN37_CHILD
}
```

Windows 검증은 실행별 임시 디렉터리만 사용하는 자식 저장 작성기에 체크포인트를 전달한다. 작성기는 같은 독점 파일 잠금을 열어 실제 저장 어셈블리의 전체 쓰기·플러시 및 원자 교체를 호출한다. 부분 쓰기는 첫 절반만 디스크에 플러시하고 임시 파일 핸들을 유지한다. 부모는 준비 신호를 받은 후 자신이 생성한 자식만 종료하고, 종료를 확인한 뒤 동일 프로필을 게임 복구 API로 다시 연다. 외부 앱·Unity 플레이어·기기 종료, 디스크 전원 손실이나 운영체제 캐시 내구성, 전체 전장 체크포인트 복원은 이 증거의 범위 밖이다. 정산을 재요청하는 테스트는 이미 확정된 동일 런 결과를 별도로 공급하며, 검증되지 않은 새 실행의 자동 정산은 승인하지 않는다.
