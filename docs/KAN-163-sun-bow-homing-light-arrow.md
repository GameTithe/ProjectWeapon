# KAN-163 태양의 활 추적 빛 화살

[KAN-163](https://gametithe.atlassian.net/browse/KAN-163)

`Assets/ProjectWeapon/Weapons/SunBow/SunBow.asset`은 4등급 `sun_bow` 원형이다. 일반 공격은 `SunBowHomingLightArrow.asset` 하나로 연결한다. 사거리 6, 초당 속도 10, 초당 회전 540도, 수명 1.2초, 피해 22, 재사용 대기시간 1.5초는 검증용 가안이다.

1. KAN-54 실행 콜백에서 `HomingLightArrowPattern.TryLaunch`에 하나의 비활성 화살과 발사 GUID를 전달한다. 반환값을 실행 결과로 사용한다. 성공하면 그 공격 요청이 선택한 유효 대상 ID가 고정되며 재사용 대기시간이 시작된다.
2. 각 고정 스텝에서 `LockedTargetId`로 현재 표적 상태를 조회하고, 발사 후 경과 시간과 함께 `Advance`에 전달한다. 조회되지 않으면 `null`을 전달한다. 살아 있고 공격 가능한 동안만 현재 위치를 추적한다. 한 번 추적을 잃으면 잃기 직전 방향으로 이동하고 피해 가능 상태를 잃는다.
3. 어댑터는 이전 위치와 새 위치 사이를 스윕하고 이동 경로 순서대로 확정된 접촉을 처리한다. 잠긴 대상이나 차단 지형을 식별한 어댑터는 같은 경과 시간과 GUID로 `ContactTarget` 또는 `ContactWorld`를 호출한다. 접촉점에 표시할 시각 효과에는 스윕의 접촉 좌표를 사용한다. 정확한 수명 경계에서는 만료가 우선한다.
4. `ProjectileLifecycle`이 첫 유효 접촉의 피해 요청 한 건과 종료 상태를 관리한다. KAN-72의 어댑터 계약대로 피해 수신 측에 요청을 전달한다. 종료된 화살을 반환할 때 `ResetForPool`을 호출한다.

## 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode editor --filter HomingLightArrowPatternTests
```

테스트는 실제 무기 에셋, KAN-54 스케줄러 및 Unity 2D 프리뷰 씬의 임시 Collider2D를 사용한다.

- 이동하는 원래 표적을 따라 회전 제한을 지키며 이동하고, 유효 접촉 전에는 피해를 반환하지 않는다.
- 첫 유효 접촉에서 피해 요청을 한 번 반환한다. 같은 경과 시간으로 반복 진행해도 이동을 누적하지 않는다.
- 표적이 사망하거나 사라지는 등의 이유로 무효화되면 진행 방향을 유지한다. 다른 대상이나 되돌아온 기존 대상에게도 피해를 반환하지 않는다.
- 차단 지형이 먼저 있는 경우에는 지형에서 종료한다. 정확히 1.2초에 도달하면 만료하고 이전 대여의 콜백은 거부한다.
- 발사 사거리 및 재사용 대기시간의 정각 경계를 통과한다. 실행이 거부된 발사는 재사용 대기시간을 소비하지 않는다.

실제 씬과 카탈로그 연결, 최종 아트·VFX, 소유권·경제·정산은 후속 범위다.
