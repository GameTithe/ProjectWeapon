# 도감 회수 비전투 해금

`CatalogEntitlements.Read`는 기존 계정 저장에서 검증된 `SaveSnapshot`을 입력으로 받는다. 원형 ID별 확정 `recovered`와 `mastered`를 각각 회수 한 종으로 센다. 단순 발견과 해석할 수 없는 저장 상태는 종수를 늘리지 않는다. 실물을 나중에 잃어도 확정 도감에서 다시 같은 권리를 읽을 수 있다. [KAN-114](https://gametithe.atlassian.net/browse/KAN-114) · [KAN-92](https://gametithe.atlassian.net/browse/KAN-92) · [KAN-103](https://gametithe.atlassian.net/browse/KAN-103)

| 회수 완료 원형 수 | 해금 | 고정 ID |
| --- | --- | --- |
| 18종 이상 | 캐릭터 외형 | `catalog_recovery_18_appearance` |
| 21종 이상 | 칭호 | `catalog_recovery_21_title` |
| 21종 이상 | 무기고 장식 | `catalog_recovery_21_armory_decoration` |

21종에 도달하면 외형 기록도 계속 반환한다. 출력은 고정 순서의 읽기 전용 권리 목록이다. 호출자는 정상 저장에서 권리를 읽어 전시나 착용 표시를 연결할 수 있다. 권리 확인은 저장 변경 없이 실행되며, 기존 영구 도감 리비전이 해금 근거가 된다. [KAN-94](https://gametithe.atlassian.net/browse/KAN-94)

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Catalog.Tests.CatalogEntitlementsTests --output "$env:TEMP/KAN-114-catalog.xml" --timeout 300
```

테스트는 17·18·20·21종에서의 경계, 숙련·발견·불명 상태의 집계와 입력 순서 변경을 확인한다. 기존 실물 한 개를 잃은 계정 저장을 새 저장소 인스턴스로 10회 열어 같은 고정 ID, 보유 무기, 정산 영수증, 잔액 및 저장 바이트를 확인한다.
