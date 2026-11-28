# KAN-102 지하 감옥 간수

`PrisonGuard.prefab`의 고정 간수는 플레이어가 4.5 월드 단위 안에 있고 견고한 벽 너머가 아닐 때 사슬 내려치기를 준비한다. 0.9초 예고가 시작되는 순간 위치와 방향을 고정한다. 주황 픽셀 톤 선의 네 꼭짓점과 피격 판정은 한 개의 3.0×1.0 직사각형을 공유한다. 마지막 변과 측면 경계를 포함하며, 대각선의 시각 좌표를 float로 바꿀 때 생기는 한 단계 반올림 오차를 허용한다. 플레이어가 옆으로 벗어나거나 벽 뒤로 이동하거나 사망하면 빗나간다. 체력 160, 피해 30, 타격 후 대기 1.8초는 검증용 설정 에셋의 값이다. [KAN-102](https://gametithe.atlassian.net/browse/KAN-102) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

- 호스트는 감옥과 같은 2D 물리 씬에서 프리팹에 `Bind(run, playerId, playerBody, playerHealth, solidWalls)`을 호출한다. 실제 감옥의 벽 TilemapCollider2D를 넘긴다. `Advance(now)`가 반환한 `PrisonGuardImpact.Hits`를 `Attack.HitsAt` 기준으로 다른 피해와 함께 판정한다. 빗나간 판정도 타격을 소비한다. [Unity PhysicsScene2D.Linecast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/linecast)
- 사망한 간수의 예고를 즉시 닫고, `Defeated`가 탐험 ID, 구역 2, 보스 ID `prison_guard`, 처치 시각을 한 번 공개한다. 호스트는 탐험별 프리팹 하나를 유지해 이 사건을 기존 진행 판정에 전달한다. 같은 탐험에서 재생성한다면 탐험 ID와 보스 ID로 중복을 막는다. 기한에 도달하면 새 예고를 시작하지 않는다. 같은 탐험이 아직 확정 전이면 늦게 관측한 기한 이전 타격을 원래 시각으로 전달한다. 기한과 같거나 늦은 타격, 확정된 결과나 바뀐 탐험의 타격은 폐기한다. 귀환 접근과 성공 조건에는 보스가 필요 없다. [KAN-86](https://gametithe.atlassian.net/browse/KAN-86) · [KAN-100](https://gametithe.atlassian.net/browse/KAN-100)
- 캐릭터는 프로젝트의 고딕 보스 컨셉에서 검은 갑옷·적갈색 망토·황동과 사슬의 대비를 따온 새 32×32, 32 PPU, Point 임시 픽셀 원본이다. 피해를 받으면 120ms 동안 붉게 표시한다. 예고선은 기존 던전 예고 소재를 재사용한다.

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.PrisonGuard.Tests.PrisonGuardTests --output "$env:TEMP/KAN-102-prison-guard.xml" --timeout 300
```

테스트는 원본 감옥 장면을 읽기 전용으로 로드하고 플레이어, 벽, 실제 귀환진에 독립 보스 프리팹을 연결한다. 정타와 범위 경계, 옆 이동, 예고 전·피격 순간 가시선, 대상 사망, 간수 처치와 중복, 최초 만료 및 탐험 교체를 확인한다. 실제 플레이어 이동으로 기존 아랫길을 돌아 귀환하며 보스가 살아 있는지 확인한다.

## 실제 감옥에 배치

간수는 기존 `Zone2Greybox.unity`의 북동쪽 선택 복도 (13.5, 8.5)에 배치했다. 서쪽 시작점 근처에서 x=4.5 복도를 따라 y=6.5까지 내려가면, 동쪽 x=8.5 복도와 y=9.5를 거쳐 간수에게 접근할 수 있다. 기존 서쪽 상자와 남쪽 보초·귀환 동선은 보스 탐지 범위를 통과하지 않는다. 간수는 경로를 막는 충돌체가 없다. [KAN-95](https://gametithe.atlassian.net/browse/KAN-95)

실제 `Zone2PrisonTrial`이 같은 플레이어와 체력·런에 간수를 연결한다. 기본 검 공격은 기존 가시선과 대상 선택에서 간수도 검사한다. 간수의 타격은 기존 적과 같은 피격 목록에서 예정 시각 순서로 처리한다. 처치 시 `BossDefeated` 이벤트와 `BossDefeat` 읽기 값을 한 번 제공한다. 이 씬은 보스를 살아 있게 두고 귀환할 수 있으며, 처치 결과는 기존 경제·정산에 쓰지 않는다. 정상 씬 테스트와 별도로 프리팹의 경계 테스트는 로드된 장면에서 시험 호스트만 비활성화한다.
