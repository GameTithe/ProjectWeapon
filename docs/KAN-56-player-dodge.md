# KAN-56 회피 동작과 피격

`PrototypeMovementSettings.asset`에 2유닛 거리, 200ms 이동, 초기 120ms 무적, 시작부터 1,250ms 재사용 대기를 설정했다. 자원은 소모하지 않는다. [KAN-56](https://gametithe.atlassian.net/browse/KAN-56) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

기존 `PrototypeMovementPlayer`의 Dynamic `Rigidbody2D`와 솔리드 `CircleCollider2D`를 사용한다. 회피 중에는 고정 스텝으로 이동 속도를 설정하고 연속 충돌 감지를 사용한다. 벽이 막으면 그 자리까지 이동한다. 회피가 끝나면 기존 이동 입력과 충돌 감지 설정을 복원한다.

## 연결

- 버튼 클릭마다 한 번 `PlayerMovement.TryDodge(run, sampledTime)`을 호출한다. 현재 데드존 통과 입력, 마지막 유효 방향, 처음에는 남쪽 순으로 시작 방향을 선택한다. 시작 뒤에는 이번 회피 방향이 고정된다.
- 귀환·입력 조정기는 `SampleDirection()`으로 물리 이동 이전 입력을 읽는다. `DodgeStarted(RunId, TimeSpan)`은 승인된 시작을 동기적으로 알리므로 물리 이동 전에 당시 귀환 시도의 취소 관측을 추가할 수 있다. [KAN-81](https://gametithe.atlassian.net/browse/KAN-81)
- 탐험 피해 수신은 `PlayerRunDamage.ApplyHit(..., dodge: movement.Dodge)`를 사용한다. 무적 구간은 시작 시각을 포함하고 120ms 경계는 제외한다. 무적 중에는 HP 변경, 피격 이벤트, 피해에 의한 귀환 취소가 모두 발생하지 않는다.
- 자동공격 스케줄러를 만들 때 `isDodgeActive: () => movement.IsDodging`을 전달한다. 회피 중 발동만 멈추며 기존 쿨다운과 켜짐 상태는 유지된다. 시각 연동에서는 `IsDodging`과 `DodgeDirection`을 읽는다. [KAN-28](https://gametithe.atlassian.net/browse/KAN-28) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.Movement.Tests --output "$env:TEMP/KAN-56-playmode.xml"
unity test . --mode EditMode --output "$env:TEMP/KAN-56-editmode.xml"
```

PlayMode는 실제 플레이어 프리팹과 2D 물리 씬에서 버튼 중복, 남쪽 기본 방향, 고정 방향, 벽 충돌, 귀환 취소 관측 순서, 재사용·탐험 만료 경계를 검증한다. EditMode는 피해의 무적 경계와 회피 중 자동공격·쿨다운을 검증한다.

Unity 6000.6.9f1에서 기존 조이스틱 기준 PlayMode 3건, 변경 후 PlayMode 6건, 전체 EditMode 402건을 통과했다.
