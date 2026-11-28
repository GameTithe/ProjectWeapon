# KAN-100 2구역 지하 감옥 회색상자

`Assets/Scenes/Zone2Greybox/Zone2Greybox.unity`는 감방 벽과 두 개의 통로가 있는 17×11 셀 검증 맵이다. 기존 32 PPU 석재·포털 검증 이미지를 사용한다. 어두운 바닥 위에서는 기존 이동 프리팹으로 걸을 수 있고, 밝은 벽 타일은 `TilemapCollider2D`에 연결돼 이동을 막는다. [KAN-100](https://gametithe.atlassian.net/browse/KAN-100) · [KAN-18](https://gametithe.atlassian.net/browse/KAN-18) · [KAN-46](https://gametithe.atlassian.net/browse/KAN-46)

| 항목 | 검증 값 |
| --- | --- |
| 시작점 | 월드 중심 (1.5, 9.5) |
| 귀환진 | 월드 중심 (15.5, 1.5), 반경 1유닛의 파란 원 |
| 구역 설정 | 권장 등급 2, 보상 등급 1~3, 제한 600초 가안 |

구역 수치는 `PrototypeZoneCatalog.asset`의 2구역을 그대로 읽는다. 장면의 `Return Point`는 귀환 중심과 반경을 제공하며, 시작·완료 판정은 `ExtractionAccess`와 `ZoneRunCountdown`의 기존 계약을 따른다. [KAN-88](https://gametithe.atlassian.net/browse/KAN-88) · [KAN-75](https://gametithe.atlassian.net/browse/KAN-75) · [KAN-89](https://gametithe.atlassian.net/browse/KAN-89)

## 에디터 재현

1. 씬을 열고 Game 뷰를 16:9로 맞춘 뒤 Play한다. 왼쪽 위 금색 점이 플레이어다.
2. 오른쪽으로 이동해 첫 감방 벽에서 멈추는지 확인한다. 대략 x=4.5까지 왼쪽으로 돌아와 첫 통로를 남쪽으로 지나고, x=2.5로 돌아 아래쪽 복도를 통해 오른쪽 아래 파란 귀환진까지 이동한다.
3. 귀환 중심과 반경의 시작 판정은 아래 PlayMode 검증에서 확인한다. 같은 탐험의 범위 안에서 귀환을 요청하면 3초에 완료한다. 만료와 완료가 정확히 동시에 도달하면 600초 만료로 처리한다.

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.Zone2Greybox.Tests --output "$env:TEMP/KAN-100-playmode.xml"
```

자동 검증은 씬을 로드한 뒤 실제 키보드 이동과 Rigidbody2D 스텝으로 한 경로를 걷는다. 시작점과 범위 밖에서 차단되는지, 반경 경계에서 허용되는지, 해당 위치에서 3초 귀환이 완료되는지 검사한다. 다른 탐험 ID 및 만료 경계도 검증한다.

Unity 6000.6.9f1에서 PlayMode 2건을 통과했다. 게임 뷰에서 시작점과 귀환진 표시를 확인했고, 오른쪽으로 이동한 실제 플레이어는 (5.66, 9.50)에서 정지했다. 벽 Tilemap과 접촉 중이었으며 Console 오류는 0건이었다.
