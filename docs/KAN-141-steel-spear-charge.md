# KAN-141 강철창 돌진 공격

[KAN-141](https://gametithe.atlassian.net/browse/KAN-141) · [KAN-143](https://gametithe.atlassian.net/browse/KAN-143) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

전용 설정 `SteelSpearCharge.asset`을 실제 방향형 스킬 실행기에 연결해 2등급 `steel_spear`의 검증용 돌진을 구현했다. 입력 방향을 고정하고 180ms 동안 최대 2.4유닛 이동한다. 플레이어의 실제 2D 충돌체를 이동 구간에 투사하여 벽에 닿기 0.01유닛 전에 멈춘다. 짧아진 돌진도 원래 시각에 실제 정지 위치에서 찌른다.

찌르기는 정지 위치의 전방 45°와 반경 2.2유닛 안에서 벽에 막히지 않는 가장 가까운 유효 적에게 44 피해 요청을 한 번 낸다. 같은 거리에서는 고정 대상 ID 순서를 사용한다. 공격 ID, 무기 실물, 런 ID와 예정 시각은 발동 당시 값을 유지한다. 공용 잠금은 800ms, 무기 실물 잠금은 6초다. 돌진은 회피 무적을 부여하지 않는다. 값은 검증판 에셋에서 조정한다.

## 격리 검증과 호스트 연결

- `SteelSpearChargeMotion`에는 같은 2D 물리 씬의 플레이어 Rigidbody2D·일반 충돌체와 방의 실제 벽 충돌체를 전달한다. `SteelSpearChargeEffect.PendingCharge`를 사용하는 동안 방은 일반 이동과 다른 무기 동작·귀환 시작을 보류하고, 정식 프레임 시각을 `Advance`에 전달한다. `AreaAt`은 같은 찌르기 부채꼴을 임시 표시에 사용할 수 있다.
- 채널이 이미 시작됐거나 런이 종료·교체됐으면 남은 돌진과 찌르기를 실행하지 않는다. 예정 타격이 마감 이상인 경우 발동을 거절하고 잠금을 쓰지 않는다. 미관측 돌진을 마감 이후에 완료하기 위해 플레이어 좌표를 소급 변경하지 않는다. 시각이 늦어도 마감 전인 경우 먼저 실제 벽을 확인한 뒤 정지 위치에서 한 번 판정한다. [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)
- 현재 테스트는 실제 플레이어 프리팹과 테스트 방의 실제 벽으로 충돌과 구간 이동을 확인한다. 분리된 시험 정의가 직렬화된 패턴을 사용한다. KAN-143의 강철창 본체 에셋과 본 씬 연결은 인계 이후 범위다. [Unity Collider2D.Cast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/collider2d/cast) · [Unity Rigidbody2D.position](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rigidbody2d/position) · [Unity PhysicsScene2D.Linecast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/linecast)

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.SteelSpearSkill.Tests --output "$env:TEMP/KAN-141-skill.xml" --timeout 300
```
