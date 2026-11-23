# KAN-129 낡은 검 기본공격

1등급 `old_sword` 원형의 `BasicAttack`에 `OldSwordMeleeSlash.asset`을 연결한다. 런타임에서 기존 시작 무기 카탈로그를 읽어 원형을 조회하고, `AutoAttackScheduler`에 넘긴다. [KAN-129](https://gametithe.atlassian.net/browse/KAN-129) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-191](https://gametithe.atlassian.net/browse/KAN-191)

| 에셋 필드 | 테스트용 가안 |
| --- | --- |
| 사거리 | 월드 단위 1.5 |
| 쿨다운 | 1,000ms |
| 전방 부채꼴 전체 각도 | 90도 |
| 요청 피해량 | 10 |

스케줄러가 고른 가장 가까운 유효 표적을 향해 공격 적용 한 건을 생성한다. `MeleeSlashPattern.TryCreateSwing`은 실제 발동의 GUID, 공격자 ID, 같은 프레임의 전체 표적 스냅샷을 받는다. 표적이 공격 원점과 겹친 경우 방향이 없으므로 그 지점에서만 접촉을 판정한다. 한 발동에서 경계를 포함해 범위 내 유효 표적에 피해 요청을 한 건씩 만들고, 중복 콜라이더는 KAN-77의 표적 ID 검증을 따른다. [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

결과에는 `ApplicationId`, `RunId`, `EffectiveAt`과 피해 요청 목록이 있다. `EffectiveAt`은 스케줄러의 `StartedAt`이다. 조립 지점은 배치를 수락한 뒤 스케줄러 실행 콜백에서 참을 반환한다. 실패한 요청은 쿨다운을 쓰지 않으며, 피해 수신기가 동일한 적용 요청의 재전달을 표적별로 구분한다. 팀별 피해 규칙이나 조작 상태와 쿨다운의 연계는 해당 계약을 따른다. [KAN-70](https://gametithe.atlassian.net/browse/KAN-70) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

## 재현 절차

```powershell
unity test . --mode EditMode --filter ProjectWeapon.AutoAttack.Tests.MeleeSlashPatternTests --output "$env:TEMP/KAN-129-results.xml"
```

| 검증 조건 | 기대 결과 |
| --- | --- |
| 실제 카탈로그에서 `old_sword` 원형을 조회 | 1등급 무기에 근접 베기 가안 에셋이 연결돼 있다. |
| 가까운 전방 표적, 양쪽 각도 경계, 반경 경계, 범위 밖 및 중복 표적 ID를 제공 | 가장 가까운 표적을 향하고, 경계 표적을 포함해 유효 표적당 요청 한 건을 현재 공격 시각에 반환한다. |
| 최초 타격 후 999ms와 1,000ms 경과 | 999ms에 대기하고 1,000ms에 새 GUID의 타격이 한 번 시작한다. |
| 귀환 중 타격 요청 또는 유효한 적용 ID가 없는 요청 | 공격 배치가 생성되지 않는다. |
