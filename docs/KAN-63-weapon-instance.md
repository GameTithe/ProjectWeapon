# KAN-63 무기 실물 식별 및 위치 계약

`WeaponInstance.Create`는 같은 무기 원형에서도 발급할 때마다 새로운 실물 ID를 생성한다. 저장된 실물을 복구하는 쪽은 `WeaponInstanceId.TryParse`와 `WeaponInstance.Rehydrate`에 저장한 ID를 넘긴다. `WeaponDefinitionId`는 무기 원형을 가리키고, `WeaponInstanceId`는 이동과 저장을 거쳐도 같은 실물을 식별한다. [KAN-19](https://gametithe.atlassian.net/browse/KAN-19) · [KAN-63](https://gametithe.atlassian.net/browse/KAN-63)

실물에는 개인 소유 지급, 탐험 획득, 대여 중 하나의 출처와 한 개의 현재 위치를 기록한다. 출처는 실물에 남기고 대여 여부는 그 값에서 읽는다. 창고는 별도 슬롯 번호를 갖지 않으며 대여품은 창고에 둘 수 없다. 장착·운반 위치는 0 이상의 슬롯 번호를, 탐험 예약 위치는 비어 있지 않은 탐험 GUID를 사용한다. 탐험 모듈의 `RunId.Value`를 예약 값에 넘길 수 있다. 실제 슬롯 배정·용량·반입·폐기 동작은 후속 작업에서 정의한다. [KAN-45](https://gametithe.atlassian.net/browse/KAN-45) · [KAN-71](https://gametithe.atlassian.net/browse/KAN-71) · [KAN-85](https://gametithe.atlassian.net/browse/KAN-85)

Unity 직렬화는 GUID의 하이픈 포함 문자열을 사용한다. 외부에서 읽어 온 실물 목록은 적용 전에 `WeaponInstanceValidator.Validate`로 검사한다. 같은 실물 ID가 두 위치에 나타나면 두 번째 항목부터 중복을 반환하며, 누락된 항목과 잘못된 필드는 별도로 보고한다. 검사 결과는 입력을 바꾸지 않는다.

## 재현 절차

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Inventory.Tests --output "$env:TEMP/KAN-63-results.xml"
```

| 입력 | 기대 결과 |
| --- | --- |
| 동일한 원형으로 두 실물 발급 | 두 실물의 ID가 다르고 중복 진단이 없다. |
| 대여품 예약 상태를 Unity JSON으로 저장하고 읽음 | 실물 ID, 원형 ID, 대여 출처 및 탐험 예약 ID가 유지된다. |
| 하나의 실물 ID가 창고와 장착에 두 번 기록됨 | 중복 위치를 포함한 두 번째 항목의 번호와 실물 ID를 보고한다. |
| 슬롯 번호와 예약 ID가 함께 있는 손상 데이터, 비어 있는 실물 ID 또는 누락된 항목 | 유효하지 않은 값과 누락을 검출한다. |
