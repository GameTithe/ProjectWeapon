# KAN-133 나무 창 연속 찌르기

[KAN-133](https://gametithe.atlassian.net/browse/KAN-133) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

1등급 `wooden_spear`의 스킬은 `WoodenSpearRepeatedThrust.asset`이다. 입력을 놓은 원점과 정규화한 전방 방향으로 0·150·300ms에 세 번 찌른다. 사거리는 2.7월드 단위, 가로 허용 폭은 좌우 각 0.25단위, 타격당 피해는 11이다. 공용 잠금 0.8초와 무기 실물별 쿨다운 5.2초는 발동부터 센다. 이 값들은 검증 가안이다.

1. `WoodenSpearThrustEffect`를 KAN-58 발동 콜백에 연결한다. 호스트의 전투 busy 판정에는 이 효과의 `IsBusy`를 포함한다. 기존 KAN-60 드래그가 수락되면 런·장착 실물·원점·방향이 고정되고 첫 찌르기가 그 프레임에 준비된다.
2. 매 프레임 런 종료·마감·귀환 확정을 먼저 처리한다. 같은 시계와 최신 표적 후보로 `TryResolveNext`를 호출한다. 전방과 양옆 허용 폭, 시전 위치로부터 사거리를 검사한 뒤 가장 가까운 유효 대상 하나에만 피해 요청을 만든다. 막힌 통로 등 장면별 대상 제외 조건은 호스트 표적 관측에서 처리한다.
3. 각 단계는 별도 적용 ID를 갖는다. 한 프레임에 여러 타격이 밀려 있으면 한 번 처리한 뒤 그 피해 결과를 반영한 표적 상태로 다음 단계를 읽는다. 늦게 관측된 피해는 실제 처리 시각을 기록한다.
4. 유효한 방향으로 시작한 찌르기는 사거리 안에 표적이 없더라도 실행되며 해당 단계는 피해 없이 끝난다. 활성 귀환 채널 중에는 이미 시작한 단계만 처리할 수 있다. 사망, 귀환 확정, 원래 탐험 제한 시각과 다른 런 진입에서는 남은 단계가 취소된다.

## 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode playmode --async-tests true --filter WoodenSpearThrustSkillTests
unity command test status --project-path .
```

실제 시작 무기 카탈로그와 터치 드래그 어댑터, 수동 스킬 실행기 및 피해 요청을 사용한다. 사거리·폭·피해·런 시계 경계, 재시도와 취소를 검증한다. 공유 출격 씬의 플레이어 연결과 최종 VFX는 후속 작업이다.
