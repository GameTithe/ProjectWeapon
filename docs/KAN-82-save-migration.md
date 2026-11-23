# KAN-82 저장 포맷 변환

현재 로컬 저장 포맷은 기존 KAN-73과 동일한 버전 `1`이다. 변환 검증을 위해 테스트에서만 이전 형식 `-1`을 만든다. 이 버전은 출시 데이터나 과거 서비스 기록을 가정하지 않는다. [KAN-82](https://gametithe.atlassian.net/browse/KAN-82) · [KAN-73](KAN-73-atomic-snapshot.md)

## 변환 계약

- 이전 테스트 파일은 현재와 같은 체크섬 봉투를 사용하며, 본문에 `fixtureId: ProjectWeapon.KAN82.TestFixture`와 양의 리비전, 10진수 문자열 `coins`, 무기 및 도감 배열을 담는다. 정산 영수증 배열이 없으면 빈 배열로 옮긴다. 존재하는 정산 영수증과 무기 실물 ID·원형 ID·상태는 같은 값으로 옮긴다.
- 호출자가 `AtomicSnapshotStore.UpgradeLegacySnapshot()`을 실행한다. 테스트 파일일 때만 유효성을 확인한 전체 스냅샷을 기존 임시 파일 플러시·원자 교체 경로로 설치하며, 데이터 리비전은 보존한다. `Load()`는 현재 포맷을 읽는다.
- 결과의 `Upgraded`는 해당 호출에서 교체한 경우만 참이다. 이미 포맷 1이거나 앞선 교체 성공 후 응답만 잃은 경우 기존 파일을 읽고 종료한다. 활성 파일이 없으면 `Snapshot`이 비어 있다.
- 미지원 버전, 잘못된 테스트 표식이나 체크섬은 `InvalidDataException`을 발생시킨다. 입력 검증 전에 원본에 쓰지 않는다. 교체 전 I/O 실패 시 테스트 원본을 다시 사용할 수 있고, 이미 교체됐다면 같은 리비전의 현재 파일을 로드한다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Save.Tests --output "$env:TEMP/KAN-82-results.xml"
```

테스트가 만든 전용 폴더에서 영수증 유무, 보존 값, 원본 바이트, 두 번 변환, 미래 버전 거부와 교체 전후 실패를 검증한다. 기존 KAN-73 저장 검증과 함께 실행한다. Android/iOS 실기기 저장소 동작은 별도 검증 범위다.
