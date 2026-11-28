# KAN-134 사냥 활 화살 3발

1등급 사냥 활 원형 `hunting_bow`의 수동 스킬을 드래그 해제에 연결한다. 같은 발동 시각과 원점에서 조준 방향 대비 -15°·0°·+15°로 한 발씩 발사한다. 각 화살은 독립 적용 ID를 가진 KAN-72 투사체다. [KAN-134](https://gametithe.atlassian.net/browse/KAN-134) · [KAN-72](https://gametithe.atlassian.net/browse/KAN-72) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

| 검증 데이터 | 값 |
| --- | --- |
| 각도와 발사 수 | -15°·0°·+15°, 각 1발 |
| 발사 시각 | 수락한 드래그 해제 시점 |
| 최대 이동 거리 | 6 유닛 |
| 속도와 수명 | 10 유닛/초, 600ms |
| 피해 | 첫 유효 접촉에 화살당 11 |
| 재사용 대기 | 공통 0.8초, 무기 실물 5.8초 |

속도는 KAN-130 사냥 활 기본 화살 검증값과 같다. 사거리와 속도로 수명을 산출한다. 각도·속도·사거리·피해·대기는 검증용 에셋 `HuntingBowTripleArrow.asset`의 값이다.

각 화살은 이동 구간에서 처음 만난 유효 표적 또는 고체 지점에서 종료한다. 같은 거리에 벽과 표적이 있으면 벽에서 종료한다. 같은 적이 여러 화살과 각각 닿으면 서로 다른 적용 ID로 피해 요청을 낸다. 정확한 수명 경계의 접촉은 만료로 처리한다. 유효 탐험의 활성 귀환 채널 중에는 이전에 수락한 화살이 진행한다. 탐험 기한 도달, 사망, 귀환 확정, 탐험 교체를 관측하면 아직 처리하지 않은 모든 화살을 폐기한다. [Unity 6.6 PhysicsScene2D.Raycast](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physicsscene2d/raycast)

## Unity 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.HuntingBowSkill.Tests.HuntingBowTripleArrowTests --output "$env:TEMP/KAN-134-playmode.xml"
```

PlayMode 테스트는 별도 2D 물리 씬에서 BoxCollider2D로 적과 벽을 재현한다. 테스트 러너의 `HuntingBowTripleArrowTests`도 같은 검증을 실행한다. 실제 `DirectionalSkillDrag`의 pointer down, drag, pointer up 경로와 사냥 활 에셋을 사용한다. 탄도를 그리는 호스트는 `GetActiveArrows()`의 위치와 ID를 사용할 수 있다.

다른 탐험 호스트에서 사용할 때는 런 시작 후 현재 씬의 `PhysicsScene2D`, 적 콜라이더 레이어, 고체 지형 레이어와 조회 함수를 제공해 `HuntingBowTripleArrowEffect`를 수동 스킬 실행기에 등록한다. 조회 함수는 변경 없이 현재 생존·적대 여부와 적 실물 ID를 돌려준다. 씬의 물리와 런 종료 신호를 처리한 뒤 같은 런 시계로 `Advance`를 호출해 접촉 시각순으로 이번 단계의 피해 요청을 받는다. 결과의 무기 실물 ID는 발동 시 캡처하므로 비행 중 장착 슬롯을 바꿔도 원래 사냥 활에 귀속된다. 호출자는 이 결과를 그 단계에서 피해 소비자에게 전달하고 임시 결과 목록을 초기화한다. 각 런마다 어댑터를 새로 구성한다.
