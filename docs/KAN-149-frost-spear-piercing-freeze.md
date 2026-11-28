# KAN-149 서리창 빙결 관통

[KAN-149](https://gametithe.atlassian.net/browse/KAN-149) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-72](https://gametithe.atlassian.net/browse/KAN-72)

4.5 유닛을 찌르는 3등급 `frost_spear`는 발동 때 선택된 방향과 사거리로 한 번 직선 판정을 한다. 검증용 수치는 사거리 4.5, 피해 14, 빙결 0.9초, 재사용 대기시간 1.25초다. `FrostSpear.asset`과 `FrostSpearPiercingFreeze.asset`은 `Assets/ProjectWeapon/Weapons/FrostSpear/`에 있다.

1. KAN-54 실행 콜백에서 현재 로컬 `PhysicsScene2D`, 차단 지형 레이어 마스크, 콜라이더를 적의 안정 ID와 현재 상태로 해석하는 함수를 `FrostSpearPattern.TryCreateStrike`에 전달한다. 같은 적의 모든 콜라이더는 같은 ID를 준다. 성공 반환은 시전 실행 및 재사용 대기시간 적용을 뜻하며 벽에 막혀 피해가 없어도 해당한다.
2. 판정은 시전 시각의 한 직선 스윕이다. 서로 다른 유효 표적은 관통하고, 솔리드 지형에서 끝난다. 같은 거리의 지형과 적은 지형을 우선한다. 시전자와 같은 위치에 있는 표적은 그 위치의 겹침만 검사한다.
3. 각 결과는 피해 요청과 빙결 해제 시각을 함께 전달한다. 이동하는 근접 적은 `MeleeEnemyActor.ApplyMovementFreezeHit`로 접수한다. 수락 즉시 물리 이동 출력을 0으로 설정하고 새로운 적중이 빙결을 연장하면 더 늦은 해제 시각을 사용한다. 정확한 해제 시각부터 기존 속도 출력을 복원한다.
4. 적의 상태 머신은 빙결 중에도 같은 시계로 계속 진행한다. 이미 시작한 공격의 예고와 피해 시점을 유지한다. 피격으로 적이 죽으면 기존 사망 처리가 적용된다.

## 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode editor --filter FrostSpearPatternTests
```

- Unity 2D 프리뷰 씬의 실제 콜라이더로 두 적, 동일 ID 콜라이더, 차단 벽, 벽과 같은 위치의 표적과 벽 뒤 표적을 검증한다.
- 표적과 원점의 겹침, 사거리 끝, 유효하지 않은 대상, 타임아웃과 쿨다운의 경계를 검증한다.
- 실제 `MeleeEnemyActor` 및 `Rigidbody2D`로 빙결의 즉시 정지와 만료, 공격 예고의 정각 피해 처리를 검증한다.

제품 씬과 카탈로그 연결, 최종 이펙트, 보상 및 정산은 후속 범위다.
