# KAN-136 철퇴 주변 충격파

일반 무기 카탈로그의 1등급 `mace`에 `MaceShockwave`를 연결한다. 수동 스킬 실행기는 `ManualSkillAim.AtSelf()`와 실제 장착 무기를 확인한 뒤 `MaceShockwaveEffect`를 호출한다. 전투 호스트는 현재 공격 가능 표적 스냅샷과 `MaceShockwaveStrike`의 `DamageRequest`를 적용할 콜백을 제공한다. [KAN-136](https://gametithe.atlassian.net/browse/KAN-136) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-135](https://gametithe.atlassian.net/browse/KAN-135)

- 발동 당시 시전자 좌표를 중심으로 1.9 월드 단위 원을 즉시 한 번 검사한다. 반경 경계가 포함되며 제스처 ID를 그 타격의 피해 적용 ID로 사용한다. 적마다 한 번씩 피해 30을 적용한다. 유효한 발동은 범위 안에 적이 없어도 완료된다.
- 실물별 재사용 시간은 5.5초이고 실행기의 기존 공용 0.8초를 사용한다. 무효 조준, 교체되거나 만료된 탐험, 잘못 붙인 에셋, 잘못된 피해 설정은 시전되지 않는다. 수치는 검증용이다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.MaceSkill.Tests.MaceShockwaveTests --output "$env:TEMP/KAN-136-mace-skill.xml" --timeout 300
```

실제 카탈로그와 기존 실행기를 연결한 검증용 전투에서 여러 방향과 정확한 반경, 같은 표적과 제스처의 중복, 피해 시각과 체력, 실물 두 개의 공용·개별 쿨다운, 무효 조준과 탐험 만료를 검증한다.
