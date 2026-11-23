# KAN-73 로컬 스냅샷 저장

`SaveSnapshot`의 한 리비전에 무기 실물, 확정 코인, 원형별 도감 상태, 탐험별 정산 영수증을 담는다. 도감 상태와 정산 결과의 내부 형식 및 판정은 호출자의 도메인이 관리한다. 저장소는 기존 무기 실물 검증을 재사용하고 원형·런·거래 식별자의 중복을 확인한다. [KAN-73](https://gametithe.atlassian.net/browse/KAN-73) · [KAN-19](https://gametithe.atlassian.net/browse/KAN-19) · [KAN-63](https://gametithe.atlassian.net/browse/KAN-63)

## 로드와 커밋

- 호출자가 정한 로컬 프로필 경로로 `AtomicSnapshotStore`를 만든다. `Load()`는 활성 파일이 없으면 `null`을 반환한다. 활성 파일은 포맷 버전과 본문 체크섬을 검증하며, 손상된 활성 파일은 `InvalidDataException`을 반환한다.
- 첫 커밋의 기대 리비전은 0이다. 이후 `Commit(expectedRevision, next)`는 기대 리비전에서 1 증가한 다음 스냅샷을 받는다. `Committed`, `AlreadyCommitted`, `RevisionConflict` 결과와 당시 활성 스냅샷을 확인할 수 있다.
- 동일 경로의 저장소 인스턴스는 프로세스 안에서 작업을 직렬화한다. 쓰는 동안 같은 경로의 전용 잠금 파일을 배타적으로 연다. 잠금이 다른 프로세스에 잡혀 있으면 I/O 오류를 반환하므로 호출자는 다시 로드한 뒤 재시도할 수 있다. 잠금 파일은 잠금 해제 뒤에도 보존한다.
- 같은 디렉터리의 새 임시 파일에 JSON 전체를 기록하고 디스크 플러시를 완료한다. 첫 커밋은 이동, 이후 커밋은 교체로 활성 파일을 설치한다. 임시 파일은 로드 후보에서 제외하며 정리 오류는 활성 파일의 판정에 관여하지 않는다.
- 커밋 중 I/O 오류가 났다면 재로드한 뒤 적용 여부를 확인한다. 활성 데이터가 동일 리비전·동일 본문인 재요청은 `AlreadyCommitted`다. 그 사이 활성 리비전이 더 높아졌다면 충돌 결과의 스냅샷에서 기존 정산 키를 조회할 수 있다. 호출자는 확정해야 할 변경 내용과 이전 정산 영수증을 포함한 전체 스냅샷을 작성한다. [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

## 검증 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Save.Tests.AtomicSnapshotStoreTests --output "$env:TEMP/KAN-73-results.xml"
```

고유한 테스트용 폴더에서 실제 파일을 만든다. 부분 기록, 활성화 전 실패, 이동·교체 직후 응답 실패를 주입하고 재로드·재시도·동시 쓰기 시 무기·코인·도감·정산 기록이 한 리비전에서 함께 읽히는지 확인한다. 미완성 임시 파일, 체크섬과 포맷 오류, 중복 정산 키도 검증한다.

파일 이동·교체 동작의 검증 범위는 Unity 6000.6.9f1 Windows Editor와 로컬 파일 시스템이다. Android/iOS 저장소 및 기기 전원 차단 내구성 검증은 후속 작업에서 수행한다. [File.Replace](https://learn.microsoft.com/dotnet/api/system.io.file.replace) · [File.Move](https://learn.microsoft.com/dotnet/api/system.io.file.move)
