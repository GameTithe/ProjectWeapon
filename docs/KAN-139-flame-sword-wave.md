# KAN-139 화염검 화염 파동

`FlameSwordWave.asset`은 2등급 `flame_sword` 검의 방향형 스킬 설정이다. KAN-60이 완료한 방향과 KAN-58이 승인한 장착 실물의 제스처를 받아 발동한다. 발동 위치와 방향을 고정하고 220ms 후 반경 4.5유닛, 전체 각도 65°인 부채꼴을 한 번 판정한다. 피해 38, 공용 잠금 800ms, 같은 무기 실물의 잠금 6초는 검증 가안이다. [KAN-139](https://gametithe.atlassian.net/browse/KAN-139) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

- 예고에 필요한 부채꼴은 `FlameSwordWaveEffect.PendingWave.Area`에서 읽는다. 움직여도 예고와 타격은 발동 지점과 방향을 유지한다. 벽을 포함한 시야 판정은 타격 처리 시점에 원래 발동 위치에서 다시 읽는다. 범위와 시야 안의 살아 있는 적 실물 ID마다 같은 파동 피해를 한 번 적용한다.
- 실행기가 승인한 발동만 공용과 장착 실물의 쿨다운을 기록한다. 조준 불량, 취소, 잘못 연결된 무기, 탐험 마감과 같거나 그 뒤에 예정되는 파동은 시작하지 않는다. 이미 발동된 파동은 결과가 확정되지 않은 같은 탐험의 귀환 채널 중에도 진행한다. 채널 중 새 수동 발동은 공용 실행기가 거부한다.
- 전투 호스트는 `Advance(observedAt)`를 런 프레임 종결 전에 호출한다. 마감 전에 예정된 파동을 뒤늦게 관측해도 아직 결과가 확정되기 전이면 처리할 수 있다. `ScheduledAt`은 런의 타격 경계를 판단한다. `ObservedAt`은 현재 관측 시각이 필요한 일반 적 피해 처리에 사용한다. 마감 시각을 검사하는 보스를 연결할 때는 보스의 관측 시각을 갱신하기 전에 예정 타격을 시간 순서로 판정해야 한다. 사망·귀환 성공·시간 초과가 확정되거나 런이 교체되면 기존 예고는 즉시 숨겨지고 새 런은 남은 파동을 갱신하지 않고도 발동할 수 있다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.FlameSwordSkill.Tests --output "$env:TEMP/KAN-139-wave-targeted.xml" --timeout 180
```

독립 검증은 실제 `FlameSwordWave.asset`을 임시 2등급 `flame_sword` 무기 정의에 연결해 장착·스킬 실행기와 적 피해 객체를 통과한다. 부채꼴 예고 데이터, 발동 수, 중복 표적과 시야 제외, 쿨다운, 지연·탐험 경계를 검증한다. 실제 무기 에셋 연결은 KAN-140 기본공격 병합을 선행한다. [KAN-140](https://gametithe.atlassian.net/browse/KAN-140) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)
