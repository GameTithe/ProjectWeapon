# KAN-92 원형별 도감 상태

도감에는 각 무기 원형의 상태를 기록한다. 하나의 원형에서 여러 실물을 획득하거나 같은 저장·정산 요청을 재시도해도 도감 종수는 한 번만 오른다. 실물을 잃더라도 이전 기록을 보존한다. [KAN-92](https://gametithe.atlassian.net/browse/KAN-92) · [KAN-49](https://gametithe.atlassian.net/browse/KAN-49)

- `RecordPickup`에는 런 슬롯에 확정 반영된 개인 실물을 전달한다. `RecordDraw`에는 지급이 확정된 뽑기 영수증을 전달한다. 둘 다 최초 발견만 반영한다. 대여 발급이나 관찰에는 상태 이벤트를 발행하지 않는다.
- `RecordRecovery`에는 성공 결과와 완료 순간 장착 또는 운반 중인 실물의 스냅샷을 전달한다. 창고·예약 상태와 대여품은 제외한다. 회수 후 실물의 소지·상실은 도감에 영향을 주지 않는다.
- `RecordVerifiedMastery`는 숙련 평가가 완료된 원형을 회수에서 숙련으로 올린다. 과제 평가와 거래·보상 트리거는 해당 후속 작업에서 연결한다. [KAN-103](https://gametithe.atlassian.net/browse/KAN-103) · [KAN-113](https://gametithe.atlassian.net/browse/KAN-113)

시작 시 `new CatalogProgress(snapshot.Catalog)`로 복원하고, 변경이 승인되면 `ToSaveRecords()`를 다음 원자 저장 스냅샷의 도감 컬렉션으로 전달한다. 기존 저장 포맷은 그대로 사용한다. 기존의 해석할 수 없는 상태 문자열은 화면 및 종수 계산에서 해석할 수 없음으로 읽고, 저장할 때 원문을 보존한다. [KAN-73](https://gametithe.atlassian.net/browse/KAN-73)

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Catalog.Tests --output "$env:TEMP/KAN-92-editmode.xml"
```
