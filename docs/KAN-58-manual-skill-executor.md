# KAN-58 수동 스킬 발동과 입력 중복 방지

[KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-62](https://gametithe.atlassian.net/browse/KAN-62) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

`ManualSkillExecutor`를 탐험마다 하나씩 만들고 현재 `RunState`, 해당 탐험의 `EquippedWeaponSelection`, 회피 상태, 표적 조회 및 `IManualSkillEffect`를 전달한다. 둘은 같은 인벤토리와 탐험 시계를 사용한다. 소유 스레드에서 해당 프레임의 런 상태와 이동·회피를 처리한 뒤 수동 요청을 제출한다.

1. 한 입력 제스처에 새 GUID를 발급하고 조준을 시작할 때 장착된 무기의 **실물** ID를 저장한다. 완료에는 같은 GUID와 실물 ID를 `TryActivate`로 전달한다. 취소는 `Cancel`에 같은 GUID를 전달한다. 재시도는 새 제스처로 한다.
2. 실행기는 탐험·회피 상태, 시각, 장착 실물과 스킬 참조, 현재 목표를 확인한다. 조준 중 무기가 교체되면 해당 입력을 거절한다. KAN-62가 실제 교체와 같은 busy 상태 및 공통·실물별 잠금을 검사한다.
3. 무기의 `Skill`은 `ManualSkillPattern`을 참조한다. 자신·정규화된 방향·현재 유효한 적·해당 사거리 안의 지면을 입력으로 지원한다. 시야 차폐·지면 적합성처럼 효과마다 다른 규칙은 `IManualSkillEffect.TryStart`가 효과를 적용하기 전에 확인한다. 적 조회는 안정 ID를 기준으로 해당 프레임의 유효 스냅샷을 반환한다.
4. `TryStart`가 효과를 시작한 경우에만 `true`를 반환한다. 그 발동 시각에서 스킬 공용·해당 무기 실물의 재사용 가능 시각을 기록한다. 콜백 중 재진입과 슬롯 교체는 무시한다. 효과가 프레임을 넘겨 진행되면 KAN-62에 전달한 busy 상태를 종료까지 유지한다.

`PreserveResourcesAndCooldowns` 정책은 실패·취소의 자원 소모를 0으로 두고 기존 잠금 시각을 유지한다. 동일 제스처에 대한 두 번째 요청은 첫 결과와 관계없이 무시된다. 스킬 기능 구현 티켓은 원형 자산마다 스킬 종류·사거리·쿨다운을 입력한다. 현재 기본값인 4유닛, 공용 0.8초, 실물별 1.5초는 에셋 검증용 값이다. 조준 UI, 구체 효과, 라이브 씬 연결은 후속 범위다.

## 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode editor --filter ManualSkillExecutorTests
unity command test run --project-path . --mode playmode --async-tests true --filter ManualSkillInputPlayModeTests
unity command test status --project-path .
```

EditMode에서 목표·회피·귀환·시각·쿨다운·취소 경계를 확인한다. PlayMode에서 장착 uGUI 버튼과 프레임 사이 반복 제스처를 함께 검증한다.
