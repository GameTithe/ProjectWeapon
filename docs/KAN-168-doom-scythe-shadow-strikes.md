# KAN-168 종말의 낫 연속 참격과 그림자 폭발

[KAN-168](https://gametithe.atlassian.net/browse/KAN-168) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

5등급 `doom_scythe`의 일반 공격 `DoomScytheShadowStrikes.asset`은 같은 원점에서 참격 두 번과 그림자 폭발 한 번을 실행한다. 참격 반경과 시전 가능 사거리는 3, 즉시 및 시전 260ms 후 피해는 각각 19다. 700ms 후 폭발 반경은 4.2, 피해는 28이다. 재사용 대기시간 2150ms를 포함해 모두 검증용 가안이다. 에셋은 `Assets/ProjectWeapon/Weapons/PrototypeDoomScythe/`에 있다.

1. KAN-54 실행 콜백에서 `DoomScythePattern.TryStart`로 즉시 참격과 다음 두 단계의 시퀀스를 만든다. 두 개가 준비되면 즉시 참격을 적용하고, 같은 탐험의 시퀀스로 후속 타격을 보관한다.
2. 같은 탐험 시계에서 마감·사망·귀환 결과 관측을 반영한 뒤, 다음 시각이 되었을 때 `TryResolveNext`를 호출한다. 그때 유효한 대상 위치와 상태를 공급한다. 각각의 피해 묶음은 고정된 시전 원점과 새 적용 ID를 사용한다. 한 표적의 중복 후보는 묶음마다 한 번만 적용한다.
3. 한 호출이 소비하는 단계는 최대 하나다. 프레임이 늦으면 확정된 실제 처리 시각과 현재 대상 상태를 사용한다. 앞선 단계의 피해를 적용하고 새 표적 상태를 만든 뒤 다음 단계를 처리한다.
4. 귀환 채널링은 새 발동을 막고 활성 탐험의 기존 후속 단계는 유지한다. 사망, 성공 정산 진입, 런의 원래 마감 시각 또는 다음 런 진입 시 남은 단계를 취소한다.

## 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode editor --filter DoomScythePatternTests
```

테스트는 실제 Unity 에셋과 좌표 표적, KAN-54 스케줄러 및 `RunState`를 사용한다. 두 원형 참격과 더 큰 폭발의 사거리·시간·대기시간 경계, 현재 표적 변화, 프레임 지연과 종료 시 취소 여부를 확인한다. 실제 플레이 씬과 카탈로그 연결 및 최종 VFX는 후속 범위다.
