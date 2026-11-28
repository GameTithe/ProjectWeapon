# KAN-91 폐성 1구역 전투·상자 배치

[KAN-91](https://gametithe.atlassian.net/browse/KAN-91) · [KAN-93](https://gametithe.atlassian.net/browse/KAN-93) · [KAN-68](https://gametithe.atlassian.net/browse/KAN-68) · [KAN-192](https://gametithe.atlassian.net/browse/KAN-192)

Unity에서 `Assets/Scenes/ZoneOneGreybox/ZoneOneGreybox.unity`를 열고 Play한다. 이동은 WASD, 회피는 Space, 근처 상자와 귀환진은 E를 누른다. 검증판은 낡은 검으로 가까운 유효 적에게 기본 공격을 한다.

| 배치 | 좌표 |
| --- | --- |
| 출발 | (3.5, 2.5) |
| 근접 경비 | (4.5, 5.5) |
| 원거리 보초 | (11.5, 6.4) |
| 테스트 상자 | (10.5, 2.5) |
| 귀환진 | (9.5, 4.5) |

벽을 위·아래로 돌아갈 수 있다. 근접 부채꼴과 원거리 직선 예고를 확인한 뒤 범위를 벗어난다. 상자는 E로 한 번 열고 목제 창에 가까이 가면 줍는다. 일반상자 데이터에서 1등급 85%, 2등급 15%를 확인할 수 있다. 이 씬의 상자는 경로 검증을 위해 KAN-192의 고정 목제 창을 사용한다. 성공·실패 정산의 저장과 로비 재출격은 KAN-193에서 연결한다.

PlayMode `ZoneOneGreyboxTests`에서 실제 씬 열기, 키보드 이동, 벽 우회, 상자 거리 및 중복 열기 차단, 양쪽 경비의 공격과 체력 변화, 물리 픽업, 3초 귀환 및 원거리 피격에 따른 귀환 취소를 검증한다. 공격 판정은 예정 시각을 보존하며 상자와 픽업은 마감 이전의 탐험에서 허용한다.
