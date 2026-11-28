# KAN-127 심연의 성역 5구역 회색상자

Unity 6000.6.9f1에서 `Assets/Scenes/Zone5Greybox/Zone5Greybox.unity`를 열고 Play한다. 이 17×11 셀 성역 검증 배치는 남쪽 진입부, 중앙을 막는 기단, 좌우 측랑, 북쪽 귀환 제단으로 구성된다. KAN-18의 모노톤 석재 및 귀환 표식과 기존 이동 프리팹을 사용한다. 벽과 바닥을 1유닛 Grid의 별도 Tilemap에 두고 벽 셀에만 실제 충돌을 둔다. [KAN-127](https://gametithe.atlassian.net/browse/KAN-127) · [KAN-18](https://gametithe.atlassian.net/browse/KAN-18) · [KAN-46](https://gametithe.atlassian.net/browse/KAN-46)

| 항목 | 검증값 |
| --- | --- |
| 구역 | 5, 심연의 성역 |
| 권장 등급과 보상 등급 | 권장 5, 보상 4–5 |
| 제한 시간 | 900초 |
| 시작점 | (8.5, 2.5) |
| 귀환 제단과 접근 반경 | (8.5, 9.5), 0.75유닛 |
| 이동 | WASD 또는 방향키 |

## 재현

1. 시작점에서 W를 누르면 중앙 기단 앞 y≈3.66에서 멈춘다.
2. A로 x≈6.5까지 이동하고 W로 y≈4.5를 지난다. A로 x≈4.5의 서쪽 측랑에 들어간다.
3. W로 북쪽 통로 y≈9.5까지 이동하고 D로 파란 귀환 제단에 도착한다. 제단의 원형 트리거는 이동을 막지 않는다.
4. PlayMode 테스트는 실제 플레이어 좌표와 씬의 제단 반경을 기존 `ExtractionAccess`에 넘긴다. 0.75유닛의 경계는 수락하고 0.76유닛, 잘못된 구역, 이미 진행 중인 시도의 재입력을 거부한다.

구역 수치는 기존 `PrototypeZoneCatalog.asset`에서 읽는다. 씬은 도보 동선과 귀환 위치 검증에 사용한다. 정식 귀환 인터랙션, 보스와 완성 자산은 후속 통합에서 연결한다. [KAN-75](https://gametithe.atlassian.net/browse/KAN-75) · [KAN-88](https://gametithe.atlassian.net/browse/KAN-88)

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.Zone5Greybox.Tests --output "$env:TEMP/KAN-127-playmode.xml"
```
