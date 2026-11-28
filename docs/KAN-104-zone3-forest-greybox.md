# KAN-104 3구역 저주받은 숲 회색상자

`Assets/Scenes/Zone3Greybox/Zone3Greybox.unity`에는 울퉁불퉁한 외곽, 밝은 공터, 갈라지는 숲길과 회색 장애물을 배치했다. 17×11 셀의 통과 가능 길과 충돌 타일을 구분하고, 기존 32 PPU 검증 텍스처와 이동 프리팹을 재사용한다. [KAN-104](https://gametithe.atlassian.net/browse/KAN-104) · [KAN-18](https://gametithe.atlassian.net/browse/KAN-18) · [KAN-46](https://gametithe.atlassian.net/browse/KAN-46)

| 항목 | 검증 값 |
| --- | --- |
| 시작점 | 월드 중심 (1.5, 7.5) |
| 귀환진 | 월드 중심 (14.5, 2.5), 반경 0.75유닛 |
| 구역 설정 | 권장 등급 3, 보상 등급 2~4, 제한 720초 가안 |

구역 수치는 `PrototypeZoneCatalog.asset`의 3구역을 읽는다. 귀환진 원은 씬의 `Return Point`와 동일한 반경으로 표시된다. [KAN-88](https://gametithe.atlassian.net/browse/KAN-88) · [KAN-75](https://gametithe.atlassian.net/browse/KAN-75)

## 재현

1. 씬을 열고 16:9 Game 뷰에서 Play한다. 금색 플레이어를 오른쪽으로 움직여 시작 공터의 장애물에 닿는다.
2. x≈3.5에서 남쪽 하단의 길로 내려간다. 길을 따라 오른쪽으로 이동하고, 파란 귀환진 아래에서 북쪽으로 들어간다.
3. 자동 검증에서 귀환 시작이 반경 밖에서 거절되고 경계에서 허용되는지 확인한다. 범위 안에서 3초 귀환을 마친다. 정확히 720초에 도달하면 완료와 경합하더라도 만료를 우선 적용한다. [KAN-89](https://gametithe.atlassian.net/browse/KAN-89)

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.Zone3Greybox.Tests --output "$env:TEMP/KAN-104-playmode.xml"
```

PlayMode에서 실제 씬의 이동 입력, Rigidbody2D와 벽 접촉, 남쪽 우회 동선, 귀환 거리 경계, 잘못된 탐험 ID 및 제한 시간을 검증한다.

Unity 6000.6.9f1에서 PlayMode 2건이 통과했다. 게임 뷰에서 공터와 귀환진을 확인했고, 오른쪽으로 이동한 실제 플레이어는 (3.66, 7.50)에서 장애물과 접촉한 채 멈췄다. Console 오류는 0건이었다.
