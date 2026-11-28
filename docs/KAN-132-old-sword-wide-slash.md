# KAN-132 낡은 검 전방 넓게 베기

일반 무기 카탈로그의 1등급 `old_sword`에 `OldSwordWideSlash`를 연결한다. KAN-58 수동 스킬 실행기는 KAN-60의 방향 입력과 장착 실물을 검증한 뒤 `OldSwordWideSlashEffect`를 호출한다. 전투 호스트는 현재 적 스냅샷을 제공하고, `OldSwordWideSlashStrike`의 각 `DamageRequest`를 `EffectiveAt`에 적용한다. [KAN-132](https://gametithe.atlassian.net/browse/KAN-132) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

- 시전 좌표와 정규화된 조준 방향을 사용하는 150° 부채꼴이다. 2.1 월드 단위까지, 조준 방향 좌우 75° 경계를 포함한다. 호스트가 공격 가능으로 표시한 살아 있는 적만 실물 표적 ID별로 한 번 맞는다.
- 유효한 방향으로 발동하면 제스처 ID를 피해 적용 ID로 쓰는 즉시 타격 1회가 생성된다. 피해는 26이다. 같은 제스처는 재발동할 수 없으며 유효 방향에 맞는 적이 없어도 타격 요청이 완료된다.
- 무기 실물별 쿨다운은 5초이고 실행기의 공용 쿨다운 가안 0.8초를 사용한다. 잘못된 방향, 다른 원형에 붙인 에셋, 잘못된 수치는 효과와 쿨다운을 시작하지 않는다. 수치는 검증용이다.
- 전투 호스트에서 연결할 때 `OldSwordWideSlashEffect`에 현재 표적을 읽는 함수와 즉시 타격을 적용하는 함수를 전달한다. 테스트는 에디터에 가져온 실제 카탈로그, 실행기, 피해를 받는 적 개체로 구성한 재현용 전투다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.OldSwordSkill.Tests.OldSwordWideSlashTests --output "$env:TEMP/KAN-132-old-sword-skill.xml" --timeout 300
```

실제 에셋 연결, 부채꼴 양쪽 경계와 반경 경계, 조준과 무관한 가까운 표적, 발동 시점의 체력 감소, 동일 표적과 제스처의 중복 억제, 실물별 쿨다운의 직전과 정확한 경계, 무효 방향·취소·잘못된 에셋 연결을 확인한다.
