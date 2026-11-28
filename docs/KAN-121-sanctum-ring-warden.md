# KAN-121 심연의 성역 고리 파동

[KAN-121](https://gametithe.atlassian.net/browse/KAN-121) · [KAN-127](https://gametithe.atlassian.net/browse/KAN-127) · [KAN-86](https://gametithe.atlassian.net/browse/KAN-86)

심연의 성역 5구역 동쪽 방 (13.85, 8.5)에 선택형 보스를 배치했다. 실제 Zone5Greybox의 이동, 석벽, 15분 런과 귀환 제단을 쓰는 독립 검증이다. 파란 톤으로 바꾼 기존 픽셀 스프라이트와 두 가는 색 선을 사용한다.

| 검증판 설정 | 값 |
| --- | --- |
| 체력 | 300 |
| 고리 안쪽과 바깥쪽 반경 | 1.35, 3.1 월드 유닛 |
| 예고, 타격, 타격 이후 대기 | 1.05초, 피해 42, 1.9초 |
| 감지 | 5.2유닛 및 가시선 |

수락 순간의 보스 위치를 중심으로 같은 두 경계를 예고와 피해에 사용한다. 고리 안쪽 빈 공간과 바깥이면 피할 수 있다. 정확한 안쪽·바깥쪽 경계는 고리에 포함한다. 가시선은 실제 석벽으로 차단하며, 수락과 타격 시각 모두 확인한다. 공격 결과는 예정 시각과 함께 기존 피격 처리에 전달한다. 늦은 관측에도 마감 이전에 예약된 타격은 유효하고 마감 정각 이후의 타격은 제외한다. 처치 또는 런 완료 시 예고가 즉시 지워진다. [Unity PhysicsScene2D.Linecast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/linecast)

## 실제 씬 재현

1. Assets/Scenes/Zone5Greybox/Zone5Greybox.unity를 Play한다. WASD로 북동쪽 방에 접근한다. 두 예고선 사이에 있으면 피해를 받고, 중심 가까이나 고리 밖으로 이동하면 피한다.
2. 중심 가까이 접근해 기존 기본 검 공격으로 체력을 줄인다. 적중 시 보스의 색이 잠깐 변한다. 한 런에서 구역·보스·시간이 붙은 처치 이벤트가 한 번 나간다.
3. 새로운 Play에서 남쪽 진입점의 중앙 기단을 따라 서쪽으로 돌아 북쪽 귀환 제단에 도착한다. E로 3초 귀환을 시작한다. 보스는 동쪽에 살아 있어도 귀환할 수 있다.

~~~powershell
unity test . --mode PlayMode --filter 'ProjectWeapon\.(Zone5Sanctum\.Tests|Zone5Greybox\.Tests)' --output "$env:TEMP/KAN-121-sanctum-boss.xml" --timeout 300
~~~

씬은 독립적인 검증 런을 사용한다. 지급·정산·실제 로비의 5구역 출격 연결은 별도 범위다.
