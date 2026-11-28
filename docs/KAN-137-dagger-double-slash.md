# KAN-137 단검 순간 연속 베기

일반 무기 카탈로그의 1등급 `dagger`에 `DaggerDoubleSlash`를 연결한다. KAN-58은 KAN-60 조준 완료의 방향과 장착 실물을 검증한 뒤 `DaggerDoubleSlashEffect`를 호출한다. 각 타격이 발생할 때 전투 호스트가 제공한 현재 표적 스냅샷을 검사한다. [KAN-137](https://gametithe.atlassian.net/browse/KAN-137) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58) · [KAN-60](https://gametithe.atlassian.net/browse/KAN-60)

- 발동 당시의 위치, 방향, 무기 실물을 잠근다. 같은 부채꼴을 즉시와 110ms 뒤에 한 번씩 검사한다. 반경 1.2 월드 단위와 90° 각도는 경계를 포함한다. 90°는 이 검증판을 위한 부채꼴 가안이다.
- 타격마다 피해 17과 별도 피해 ID를 사용한다. 하나의 타격에서는 같은 표적을 한 번만 처리한다. 표적이 범위에 두 번 놓이면 두 타격을 모두 받고, 두 번째 타격 전에 벗어나거나 사망하면 그 타격만 빗나간다.
- 실물마다 재사용 시간 4.8초와 기존 실행기의 공용 0.8초를 적용한다. 발동이 수락된 뒤 다른 무기로 바꿔도 두 번째 타격은 최초 단검 실물에 귀속된다.
- 전투 프레임은 런 시계로 `Advance(now)`를 호출한다. 유효 기간 내 프레임이 늦어지면 현재 후보를 검사하고 예정된 피해 시각을 전달하므로, 이후 시각의 피해보다 먼저 호출해야 한다. 최초 호출이 정확한 만료 시각이거나 그 이후인 경우, 이미 탐험이 끝났거나 다른 탐험이 시작된 경우에는 밀린 타격을 제거한다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.DaggerSkill.Tests.DaggerDoubleSlashTests --output "$env:TEMP/KAN-137-dagger-skill.xml" --timeout 300
```

실제 카탈로그와 스킬 실행기에 기존 적 피해 수신기를 연결한다. 반경과 각도 경계, 두 타격과 피해 수치, 표적의 진입·이탈, 무기 교체, 정확한 재사용 시간, 중복 제스처, 무효 조준, 탐험 만료·사망·교체를 검증한다.
