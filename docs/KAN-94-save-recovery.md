# KAN-94 손상 저장 복구

`AtomicSnapshotStore.LoadWithRecovery`는 현재 빌드의 무기 원형 ID를 확인할 수 있는 카탈로그 조회 함수를 받는다. 활성 파일의 UTF-8·JSON·포맷·체크섬·필수 데이터·중복 ID를 확인한 뒤 저장된 무기 실물, 도감, 뽑기 영수증이 참조하는 원형을 조회한다. 활성 파일이 없거나 손상되면 같은 검사를 통과한 백업을 반환한다. 기존 `Load()`는 활성 파일만 읽으며, 기존 `Commit()`의 리비전 규칙을 사용한다. 10회 추첨 영수증은 모든 지급품의 원형을 검증한다. 이미 소비되거나 상실해 현재 인벤토리에 없는 실물이라도 원형이 유효하면 영수증을 유지한다. [KAN-109](https://gametithe.atlassian.net/browse/KAN-109) [KAN-94](https://gametithe.atlassian.net/browse/KAN-94) · [KAN-73](https://gametithe.atlassian.net/browse/KAN-73) · [KAN-82](https://gametithe.atlassian.net/browse/KAN-82)

| 상태 | 결과 |
| --- | --- |
| `Healthy` | 검증된 활성 스냅샷을 반환한다. |
| `Missing` | 활성과 백업 파일이 모두 없다. |
| `RecoveredFromBackup` | 검증된 직전 백업을 반환한다. `ActiveIssue`에 활성 파일의 문제를 기록한다. 백업은 활성보다 이전 리비전일 수 있다. |
| `Corrupt` | 검증을 통과한 사본이 없다. `ActiveIssue`와 `BackupIssue`로 원인을 확인한다. |
| `UnsupportedVersion` | 내용 체크섬을 확인할 수 있지만 현재 읽지 않는 포맷이다. 이전 백업으로 자동 전환하지 않는다. |

첫 저장에는 이전 파일이 없고, 이후 원자 교체는 직전 활성 파일을 같은 디렉터리에 `.bak`로 남긴다. 복구 조회는 어느 파일에도 쓰지 않는다. 손상된 활성 파일은 원래 경로에 그대로 있으므로 오류를 조사하거나 후속 복구 절차에 사용할 수 있다. 호출자는 복구 결과를 이전 리비전으로 표시하고, 확인 전 새 저장을 시작하지 않는다. 저장 권위와 탐험 재개 흐름은 후속 작업에서 연결한다. [KAN-189](https://gametithe.atlassian.net/browse/KAN-189)

## 검증 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Save.Tests --output "$env:TEMP/KAN-94-results.xml"
```

서로 다른 리비전을 로컬 임시 폴더에 커밋한 후 활성 파일을 절단하거나, 올바른 체크섬을 가진 중복 실물 ID와 미등록 원형 참조를 주입한다. 복구 결과의 리비전·확정 코인·실물 ID·도감·영수증을 대조하고 활성 원본과 백업 바이트가 그대로인지 확인한다. 백업 없음·백업 손상·활성 파일 없음·미지원 미래 포맷과 반복 조회도 검증한다. [KAN-37](https://gametithe.atlassian.net/browse/KAN-37)
