# KAN-93 버려진 폐성 1구역 회색상자

Unity에서 `Assets/Scenes/ZoneOneGreybox/ZoneOneGreybox.unity`를 직접 열고 Play를 실행한다. KAN-18의 32 PPU 석재·귀환진 검증 에셋과 기존 이동 프리팹을 재사용했다. 바닥과 벽은 1유닛 Grid의 별도 Tilemap이고, 벽 셀에는 Grid 충돌을 적용한다. [KAN-93](https://gametithe.atlassian.net/browse/KAN-93) · [KAN-18](https://gametithe.atlassian.net/browse/KAN-18) · [KAN-46](https://gametithe.atlassian.net/browse/KAN-46)

| 항목 | 배치와 수치 |
| --- | --- |
| 시작점 | (3.5, 2.5) |
| 귀환진 | (9.5, 4.5), 접근 반경 0.75유닛 |
| 우회 통로 | 중앙 벽의 아래쪽 y=1.5 또는 위쪽 y=6.5 |
| 구역 데이터 | 기존 1구역 카탈로그의 권장 등급 1, 보상 등급 1–2, 제한 시간 480초 |
| 조작 | WASD 또는 방향키로 이동, E 또는 게임패드 북쪽 버튼으로 귀환 시작 |

## 재현

1. 시작점에서는 귀환 버튼을 눌러도 채널링이 시작되지 않는다.
2. D로 오른쪽에 닿으면 중앙 벽 앞에서 멈춘다. S로 아래 통로까지 내려가고 D를 눌러 벽을 지나서 W로 파란 귀환진에 접근한다.
3. 반경에 들면 귀환진이 밝아진다. E를 누르면 기존 접근 판정과 3초 채널링이 시작한다. 검증 중 귀환진에서는 멈춘 상태로 관찰한다.
4. PlayMode 테스트에서 반경 경계 0.75유닛을 허용하고, 바깥 0.76유닛 및 동일 채널링 요청의 재입력을 거부하는지 확인한다.

이 씬은 타일 충돌, 도보 동선, 귀환 시작 검증에 사용한다. 전투, 상자, 결과 정산과 실제 런 조립은 1구역 통합 티켓에서 연결한다. [KAN-75](https://gametithe.atlassian.net/browse/KAN-75) · [KAN-89](https://gametithe.atlassian.net/browse/KAN-89) · [KAN-91](https://gametithe.atlassian.net/browse/KAN-91)

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.ZoneOneGreybox.Tests --output "$env:TEMP/KAN-93-playmode.xml"
```
