# KAN-54 자동 기본공격

[Jira KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

## 게임 코드 연결

- 출격 조정 코드에서 활성 Run의 `RunState`와 기본공격 실행 콜백을 `AutoAttackScheduler`에 연결한다. ON/OFF UI는 `TrySetEnabled`를 호출하고 `IsEnabled`를 선택값으로 읽는다. `IsEffective`는 탐험 상태에서 해당 선택이 실행에 반영되는지 나타낸다.
- 한 프레임의 탐험 시간을 샘플링한다. 귀환 시작을 Run에 반영하고, 취소·사망 신호를 한꺼번에 `ExtractionChannel.ProcessFrame`에 전달해 프레임 상태를 확정한다. 같은 시간으로 `TryAttack`을 호출하면서 현재 장착 `WeaponDefinition`, 공격 원점, 표적 후보 목록을 전달한다. 후보 목록에는 표적의 안정적인 ID와 월드 좌표, 생존 여부 및 공격 가능 여부를 담는다.
- 무기의 `BasicAttack`에는 `BasicAttackPattern`을 상속한 공격별 데이터 에셋을 연결한다. 사거리는 월드 좌표 단위이며 0 이상이다. 쿨다운은 양의 정수 밀리초다. 새 공격 에셋의 초기값 1.5 유닛과 1000 ms는 편집 가능한 가안이다. KAN-129가 낡은 검의 실제 패턴과 데이터 연결을 맡는다.
- 실행 콜백이 받는 `BasicAttackRequest`에는 탐험 ID, 무기 원형 ID, 패턴, 공격 원점, 선택된 표적 및 발동 시점이 들어 있다. 콜백은 해당 공격을 동기적으로 시작하고 `true`를 반환한다. 실행을 받아들일 수 없으면 `false`를 반환하며 부수효과를 남기지 않는다.

사거리 안에서 가장 가까운 유효 표적을 기존 KAN-52 선택기로 고른다. 발동된 공격의 쿨다운을 발동 시점부터 계산한다. 표적이나 유효한 패턴이 없거나 실행이 거부되면 쿨다운은 시작되지 않는다. 프레임이 지연되면 현재 프레임에서 발동 조건을 한 번 검사한다. OFF와 채널링 중에도 시계는 흐른다. 채널링 동안의 토글 요청은 거부하고, 취소되어 탐험으로 돌아오면 채널링 직전 설정을 다시 적용한다. 성공·실패 결과 또는 제한시간 도달 시 실행을 차단한다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests --output "$env:TEMP/KAN-54-auto-attack/editmode.xml" --timeout 900 --non-interactive
```

테스트는 실제 `WeaponDefinition`, `RunState`, `ExtractionChannel`과 실행 기록을 남기는 대역 콜백을 사용한다. 2유닛 사거리와 500ms 쿨다운을 주입하고 사거리 및 쿨다운 경계, 같은 시점 연속 호출, ON/OFF, 이전 토글 상태 복귀, 채널링 완료와 사망·만료 차단, 무기·표적 오류 및 실행 거부를 확인한다.

Unity 6000.6.9f1 로컬 EditMode 전체 380건이 통과했다. 이 티켓의 자동공격 테스트는 11건이며 전체 실패와 건너뜀은 각각 0건이다.
