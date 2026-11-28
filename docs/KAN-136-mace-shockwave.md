# KAN-136 철퇴 주변 충격파

일반 무기 카탈로그의 1등급 `mace`에 `MaceShockwave`를 연결한다. 1구역 전투의 수동 스킬 실행기는 `ManualSkillAim.AtSelf()`와 장착 무기를 확인한다. `ZoneOneEncounter`는 충격파 또는 낡은 검의 스킬에 현재 살아 있고 시전 위치에서 보이는 적을 공급하고, 결과의 피해 요청을 기존 적 피해 경로로 적용한다. [KAN-136](https://gametithe.atlassian.net/browse/KAN-136) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-135](https://gametithe.atlassian.net/browse/KAN-135)

- 발동 당시 시전자 좌표를 중심으로 1.9 월드 단위 원을 즉시 한 번 검사한다. 반경 경계가 포함되며 제스처 ID를 그 타격의 피해 적용 ID로 사용한다. 적마다 한 번씩 피해 30을 적용한다. 유효한 발동은 범위 안에 적이 없어도 완료된다.
- 실물별 재사용 시간은 5.5초이고 실행기의 기존 공용 0.8초를 사용한다. 무효 조준, 교체되거나 만료된 탐험, 잘못 붙인 에셋, 잘못된 피해 설정은 시전되지 않는다. 수치는 검증용이다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.MaceSkill.Tests.MaceShockwaveTests --output "$env:TEMP/KAN-136-mace-skill.xml" --timeout 300
unity test . --mode PlayMode --filter ProjectWeapon.MaceSkill.Tests.MaceCombatHudTests --output "$env:TEMP/KAN-136-integrated-playmode.xml" --timeout 240
```

실제 카탈로그와 기존 실행기를 연결한 검증용 전투에서 여러 방향과 정확한 반경, 같은 표적과 제스처의 중복, 피해 시각과 체력, 실물 두 개의 공용·개별 쿨다운, 무효 조준과 탐험 만료를 검증한다.

PlayMode에서는 로비의 장착·출격 버튼으로 철퇴 실물을 들고 1구역에 입장한다. 실제 터치로 자기 중심 스킬을 발동하면 1.7 월드 단위 떨어진 보스의 체력이 90에서 60으로 감소한다. 이 거리는 철퇴 기본 공격 반경 1.5보다 멀다. 재사용 대기 중 스킬 버튼은 비활성화되며 재입력 시 체력이 유지된다.
