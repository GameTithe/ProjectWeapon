# KAN-110 4구역 마왕의 요새 회색상자

`Assets/Scenes/Zone4Greybox/Zone4Greybox.unity`는 세 개의 방과 높이가 다른 두 통로를 배치한 19×11 셀 검증 맵이다. 따뜻한 색조의 바닥, 밝은 통로, 충돌 벽을 구분하고 기존 32 PPU 석재·귀환진과 이동 프리팹을 재사용한다. [KAN-110](https://gametithe.atlassian.net/browse/KAN-110) · [KAN-18](https://gametithe.atlassian.net/browse/KAN-18) · [KAN-46](https://gametithe.atlassian.net/browse/KAN-46)

| 항목 | 검증 값 |
| --- | --- |
| 시작점 | 월드 중심 (1.5, 4.5) |
| 귀환진 | 월드 중심 (15.5, 3.5), 반경 0.75유닛 |
| 구역 설정 | 권장 등급 4, 보상 등급 3~5, 제한 720초 가안 |

구역 수치는 `PrototypeZoneCatalog.asset`의 4구역을 읽는다. 파란 원과 `Return Point`의 진입 범위는 일치한다. [KAN-88](https://gametithe.atlassian.net/browse/KAN-88) · [KAN-75](https://gametithe.atlassian.net/browse/KAN-75)

## 재현

1. 씬을 열고 16:9 Game 뷰에서 Play한다. 서쪽 방에서 금색 플레이어를 오른쪽으로 이동해 구획 벽과의 접촉을 확인한다.
2. x≈3.5에서 위쪽으로 이동해 y≈6.5의 첫 문을 지난다. 중앙 방에서 y≈4.5까지 내려가 두 번째 문을 지나 오른쪽 방으로 들어간다. 귀환 표식 위에서 남쪽으로 진입한다.
3. 아래 테스트에서 반경 안의 3초 귀환, 반경 경계·바깥, 탐험 ID와 720초의 만료 우선순위를 확인한다. [KAN-89](https://gametithe.atlassian.net/browse/KAN-89)

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.Zone4Greybox.Tests --output "$env:TEMP/KAN-110-playmode.xml"
```

자동 검증은 씬을 불러온 뒤 기존 이동 입력과 Rigidbody2D를 사용해 두 문을 지나 실제 귀환 위치까지 걷는다. 귀환 판정은 `ExtractionAccess`와 `ZoneRunCountdown`에 씬의 좌표를 전달해 검사한다.

Unity 6000.6.9f1에서 PlayMode 2건이 통과했다. 게임 뷰에서 두 문과 귀환 범위가 표시됐고, 오른쪽으로 이동시킨 실제 플레이어는 (4.66, 4.50)에서 구획 벽과 접촉한 채 멈췄다. Console 오류는 0건이었다.
