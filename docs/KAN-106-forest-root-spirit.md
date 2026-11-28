# KAN-106 저주받은 숲 뿌리 정령

[KAN-106](https://gametithe.atlassian.net/browse/KAN-106) · [KAN-104](https://gametithe.atlassian.net/browse/KAN-104) · [KAN-108](https://gametithe.atlassian.net/browse/KAN-108) · [KAN-86](https://gametithe.atlassian.net/browse/KAN-86)

`Assets/ProjectWeapon/ForestBoss/ForestRootSpirit.prefab`은 3구역 단일 보스 프리팹이다. 프로젝트의 고딕 보스 콘셉트에서 어두운 실루엣과 명암 대비를 참고해, 갈라진 가지와 자주색 빛이 있는 32×32 원본 픽셀을 제작했다. 텍스처는 32 PPU·Point·압축 없음으로 불러온다.

| 검증판 설정 | 값 |
| --- | --- |
| 체력 | 205 |
| 감지 | 5.25유닛과 벽 가시선 |
| 저주 뿌리 | 대상의 수락 위치에 고정된 반경 1.3유닛 |
| 예고 / 피해 / 회복 | 1.0초 / 34 / 타격 뒤 1.65초 |

예고와 피해는 하나의 확정 중심·반경을 사용한다. 피격 판정은 부동소수점 표현 오차를 허용하고, 실제 벽 뒤나 원 밖으로 이동하거나 사망한 대상은 제외한다. 전투 호스트는 한 탐험에 한 프리팹을 만들고 `Bind`로 플레이어와 체력·벽을 연결한다. 발생한 `ForestRootImpact`는 타격 예정 시각과 피해 요청을 포함한다. 호스트가 다른 피격과 예정 시각 순서대로 처리한 뒤 프레임의 귀환·실패를 판정한다. 예정 시각이 정확한 탐험 만료 이상이면 취소하고 종료 전에 예정된 타격은 늦게 관측해도 원래 시각으로 전달한다. `Defeated` 및 `Defeat`는 탐험·구역 3·`forest_root_spirit`·시각을 한 번 기록한다. [Unity 2D Linecast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/linecast)

## 검증과 배치 후보

PlayMode 검증은 실제 숲 장면을 읽어 원본 타일맵 충돌과 이동 프리팹을 사용하고, 임시 인스턴스로 북동쪽 (13.5, 8.5)에 뿌리 정령을 배치한다. 북동쪽에서 예고를 띄우고 경계와 회피·시야 차단을 확인한다. 같은 지도에서 기존 남쪽길을 실제 입력으로 걸어 체력 205의 정령을 두고 귀환한다. 3구역 본 씬 연결은 KAN-108 병합 뒤 별도 통합에서 재현한다.

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.ForestBoss.Tests --output "$env:TEMP/KAN-106-forest-boss.xml" --timeout 240
```
