# KAN-144 연사궁 다중 화살

[요구사항과 결정](https://gametithe.atlassian.net/browse/KAN-144)

3단계 검증 기준은 방향 조준 확정, 투사체 다섯 발의 동시 생성, 각 투사체의 일반 충돌 적용이다. 이 스킬은 2등급 활 `repeater_bow`의 전용 방향형 스킬이다.

| 검증 항목 | 직렬화된 설정 |
| --- | --- |
| 한 번 발동 시 화살 각도 | 조준 방향 기준 −20°, −10°, 0°, +10°, +20° |
| 최대 이동 거리 / 속도 | 6.8 유닛 / 초당 13 유닛 |
| 화살 한 발 피해 | 8 |
| 처음 맞은 실물 표적 | 같은 피해 적용 ID로 한 번까지만 적용 |
| 공용 / 실물 무기 쿨다운 | 0.8초 / 6초 |

`RepeaterBowVolley.asset`의 수치는 플레이 검증용이다. 화살의 유효 시간은 거리와 속도에서 계산하며 약 523.077ms이다. 수명 정각과 런 마감 정각의 접촉은 제외한다. 같은 런의 결과가 아직 확정되지 않았다면 마감 정각이나 이후의 첫 갱신에서도 마감 전 구간까지만 검사하고, 그 구간의 접촉을 계산된 시각으로 보고한다. 다른 런으로 교체되거나 사망 등으로 결과가 이미 확정되면 남은 발을 폐기한다. 이미 발동한 화살은 살아 있는 같은 런의 귀환 채널 중에도 진행한다.

## 전투 연결

`ManualSkillExecutor`가 추적하는 조준 제스처로 한 번 발동한다. `RepeaterBowVolleyEffect`는 런과 `PhysicsScene2D`를 받아 `ProjectileLifecycle`로 각 발을 이동시킨다. 목표 레이어의 Collider를 실물 `TargetCandidate`로 조회하는 읽기 전용 함수와 별도의 지형 레이어를 전달한다. 이 함수에서 적대 대상 여부와 현재 공격 가능 여부를 반영한다. 현재 폐성의 벽 TilemapCollider2D와 플레이어·적 객체는 Default 레이어를 공유한다. 정식 연결에서는 지형이 아닌 고체 충돌체가 벽으로 오인되지 않도록 벽과 표적을 구분하는 충돌 레이어를 구성해야 한다. 같은 거리에서는 지형이 먼저 막는다. 자기 자신과 우호·무효 대상, 지형 레이어의 Trigger는 지나간다. 한 실물에 화살 다섯 발이 실제로 닿으면 총 피해는 40이다. 같은 화살에서 해당 실물의 여러 Collider가 발견돼도 같은 적용으로 한 번만 처리한다.

호스트가 `Advance`에 현재 런 시각과 명중 목록을 제공한다. 효과가 계산된 접촉 시각 순서로 목록에 명중을 추가한다. 정식 호스트는 마감 확정 전에 이 목록을 수집하고 `ContactAt`을 기준으로 같은 프레임의 사망·종료 관측과 판정 순서를 맞춘다. 마감 프레임의 투사체는 선분 레이로 마지막 구간을 검사한다. 애니메이션이나 검증용 표시에는 `GetActiveArrows()`로 현재 위치와 속도를 읽을 수 있다. KAN-142가 연사궁의 기본 공격 및 무기 정의를 소유하며, 해당 에셋이 들어온 뒤 이 스킬 참조를 연결할 수 있다.

## 재현

Unity `6000.6.9f1`에서 다음 검증을 실행한다.

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.RepeaterBowSkill.Tests --output "$env:TEMP/KAN-144-volley-targeted.xml" --timeout 180
```

실제 로컬 2D Physics Scene과 직렬화된 스킬 에셋을 사용하는 PlayMode 사례가 탄착, 지형 우선순위, 한 발당 피해 적용 ID, 쿨다운과 런 경계를 검증한다.

- [KAN-60 방향 조준](https://gametithe.atlassian.net/browse/KAN-60)
- [KAN-72 투사체 생명주기](https://gametithe.atlassian.net/browse/KAN-72)
- [KAN-142 연사궁 기본 공격](https://gametithe.atlassian.net/browse/KAN-142)
- [Unity 6.6 PhysicsScene2D.Raycast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/raycast)
