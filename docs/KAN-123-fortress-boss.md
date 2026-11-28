# KAN-123 마왕의 요새 검격 보스

`Assets/Scenes/Zone4Greybox/Zone4Greybox.unity` 북동쪽 (17.5, 9.5)에 요새 검격 보스를 배치했다. 기존 두 문과 (15.5, 3.5) 귀환진은 그대로 연결된다. 동쪽 방에 들어온 높이에서 남쪽 귀환진으로 이동하면 체력 250의 보스를 둔 채 귀환할 수 있다. 보스의 임시 외형은 기존 경비 스프라이트, 예고와 피격 스파크는 KAN-29의 EnemyCue 프리팹을 재사용한다. [KAN-123](https://gametithe.atlassian.net/browse/KAN-123) · [KAN-110](https://gametithe.atlassian.net/browse/KAN-110) · [KAN-29](https://gametithe.atlassian.net/browse/KAN-29)

| 항목 | 검증값 |
| --- | --- |
| 감지 | 4.7유닛 및 벽 가시선 |
| 검격 | 고정 원점·방향, 반경 2.6유닛, 각도 120° |
| 예고 및 대기 | 0.9초 및 타격 뒤 1.65초 |
| 체력 및 단일 피해 | 250 및 38 |

`Zone4FortressTrial`은 카탈로그의 12분을 쓰며 플레이어, 타일 벽, 귀환 접근, 실제 낡은 검 자동공격을 같은 독립 런으로 연결한다. 예고와 피해는 같은 부채꼴 위치를 사용한다. 그림의 부동소수점 경계는 반경 0.000002유닛 및 각도 0.0002°의 비교 오차를 허용한다. 타격 순간에 부채꼴 밖으로 이동하거나 벽이 가리면 피해가 없으며, 실제 피해가 채널과 같은 시각이면 기존 귀환 취소 규칙을 쓴다.

같은 런에서 예정 타격이 마감 전이면 늦게 처리되더라도 예정 시각으로 전달한다. 예정 타격이 마감과 같거나 이후로 가는 예고는 시작하지 않는다. 보스가 처치되면 예고를 지우고 탐험 ID, 구역 4, `fortress_cleave_boss`, 처치 시각을 이벤트로 한 번 전달한다. 검증판의 로컬 씬 런은 결과를 저장하지 않는다. [KAN-190](https://gametithe.atlassian.net/browse/KAN-190) · [KAN-86](https://gametithe.atlassian.net/browse/KAN-86)

## 재현

1. 씬을 열어 Play한 뒤 KAN-110의 두 문을 지난다. 오른쪽 방에서 (17.5, 7.3) 쪽으로 북상하면 보스가 현재 방향에 부채꼴을 표시한다. 표시 안에 머무는 경우와 옆으로 피하는 경우를 비교한다.
2. 보스에게 가까이 가면 실제 낡은 검이 자동으로 공격한다. 검격이 이어지는 동안 영역 밖으로 움직여 타격을 피한다.
3. 보스를 우회하려면 두 번째 문 높이에서 동쪽 귀환진 열까지 이동한 후 남쪽으로 접근한다. E를 누르고 3초 동안 정지한다.

```powershell
unity test . --mode PlayMode --filter 'FortressBossTests|Zone4GreyboxTests' --output "$env:TEMP/KAN-123-playmode.xml" --timeout 900
```
