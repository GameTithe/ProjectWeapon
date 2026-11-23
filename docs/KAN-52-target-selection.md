# KAN-52 자동공격 표적 선택 검증

Unity 어댑터는 표적의 안정적인 ID, XY 월드 좌표 기준점, 생존 및 공격 가능 여부를 `TargetCandidate`로 전달한다. 같은 표적에 콜라이더가 여러 개 있으면 같은 ID와 기준점을 제공한다. `TargetSelector.FindNearest`는 공격 원점과 검사 반경을 같은 월드 단위로 받고, 유효 후보 중 거리가 가장 가까운 표적 한 개를 반환한다. 후보가 없으면 `null`을 반환한다. [KAN-19](https://gametithe.atlassian.net/browse/KAN-19) · [KAN-52](https://gametithe.atlassian.net/browse/KAN-52)

좌표와 사거리는 유한한 값이어야 하며 사거리는 0 이상이다. ID가 비었거나 좌표가 유한하지 않은 후보는 제외한다. 후보가 없을 때는 빈 목록을 전달한다. 목록 자체가 `null`이면 호출 오류를 반환한다. 거리의 제곱은 `double`로 계산한다.

## 재현 절차

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Combat.Tests --output "$env:TEMP/KAN-52-results.xml"
```

| 조건 | 기대 결과 |
| --- | --- |
| 더 가까운 사망·공격 불가 표적과 유효 표적이 함께 있음 | 살아 있고 공격 가능한 가장 가까운 표적을 선택한다. |
| 원점 `(0, 0)`, 범위 5, 후보 `(3, -4)` | 제곱거리가 25라서 경계 표적을 포함한다. `(3, -4.01)`은 범위를 벗어난다. |
| 원점과 후보가 같은 위치에 있고 사거리가 0 | 같은 위치의 후보만 선택 대상으로 남는다. |
| 같은 거리의 `A-9`와 `a-1`의 후보 순서를 바꿈 | ID 서수 비교에 따라 항상 `A-9`를 선택한다. |
| 빈 후보, 유효하지 않은 원점·사거리 또는 무효 표적만 존재 | 선택 결과가 없다. |

선택기는 표적 값과 조회 결과만 반환한다. 공격 실행, 피해 적용, 표적 목록 수집 및 타이머는 후속 기능에서 연결한다. [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)
