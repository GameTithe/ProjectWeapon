# KAN-102 지하 감옥 간수

`PrisonGuard.prefab`의 고정 간수는 플레이어가 4.5 월드 단위 안에 있고 견고한 벽 너머가 아닐 때 사슬 내려치기를 준비한다. 0.9초 예고가 시작되는 순간 위치와 방향을 고정한다. 주황 픽셀 톤 선의 네 꼭짓점과 피격 판정은 한 개의 3.0×1.0 직사각형을 공유한다. 마지막 변과 측면 경계도 포함한다. 플레이어가 옆으로 벗어나거나 벽 뒤로 이동하거나 사망하면 빗나간다. 체력 160, 피해 30, 타격 후 대기 1.8초는 검증용 설정 에셋의 값이다. [KAN-102](https://gametithe.atlassian.net/browse/KAN-102) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

- 호스트는 감옥과 같은 2D 물리 씬에서 프리팹에 `Bind(run, playerId, playerBody, playerHealth, solidWalls)`을 호출한다. 실제 감옥의 벽 TilemapCollider2D를 넘긴다. `Advance(now)`가 반환한 `PrisonGuardImpact.Hits`를 `Attack.HitsAt` 기준으로 다른 피해와 함께 판정한다. 빗나간 판정도 타격을 소비한다. [Unity PhysicsScene2D.Linecast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/linecast)
- 사망한 간수의 예고를 즉시 닫고, `Defeated`가 탐험 ID, 구역 2, 보스 ID `prison_guard`, 처치 시각을 한 번 공개한다. 호스트는 이 사건을 기존 진행 판정에 전달할 수 있다. 같은 탐험의 기한 또는 결과 확정 뒤 예고·피해를 처리하지 않는다. 귀환 접근과 성공 조건에는 보스가 필요 없다. [KAN-86](https://gametithe.atlassian.net/browse/KAN-86) · [KAN-100](https://gametithe.atlassian.net/browse/KAN-100)
- 캐릭터는 프로젝트의 고딕 보스 컨셉에서 검은 갑옷·적갈색 망토·황동과 사슬의 대비를 따온 새 32×32, 32 PPU, Point 임시 픽셀 원본이다. 피해를 받으면 120ms 동안 붉게 표시한다. 예고선은 기존 던전 예고 소재를 재사용한다.

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.PrisonGuard.Tests.PrisonGuardTests --output "$env:TEMP/KAN-102-prison-guard.xml" --timeout 300
```

테스트는 원본 감옥 장면을 읽기 전용으로 로드하고 플레이어, 벽, 실제 귀환진에 독립 보스 프리팹을 연결한다. 정타와 범위 경계, 옆 이동, 예고 전·피격 순간 가시선, 대상 사망, 간수 처치와 중복, 최초 만료 및 탐험 교체를 확인한다. 실제 플레이어 이동으로 기존 아랫길을 돌아 귀환하며 보스가 살아 있는지 확인한다.
