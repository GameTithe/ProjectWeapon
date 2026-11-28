# KAN-62 전투 장착 무기 교체

`EquippedWeaponSelection`은 탐험에 장착된 두 슬롯을 조회하고 다음 공격에 사용할 실물 ID와 무기 원형을 반환한다. 첫 번째 유효 장착 슬롯으로 시작한다. 교체 시 슬롯에서 원형을 읽고 인벤토리 상태를 그대로 유지한다. [KAN-62](https://gametithe.atlassian.net/browse/KAN-62) · [KAN-71](https://gametithe.atlassian.net/browse/KAN-71) · [KAN-190](https://gametithe.atlassian.net/browse/KAN-190)

## 연결

- 탐험 시작 후 해당 `RunState`, `InventoryService`, `WeaponCatalog`, 공격·스킬 동작 중 여부를 읽는 콜백과 새 `WeaponActionLocks`를 전달한다.
- HUD의 장착 아이콘 두 개를 `EquippedWeaponButtons.Bind(selection, firstButton, secondButton, sampleCombatClock)`으로 연결한다. 슬롯 인덱스는 0부터 시작한다. 실제 변경이 일어나면 `ActiveSlotChanged`가 현재 슬롯, 실물 ID, 원형을 알린다.
- 공격/스킬 시작 직전 현재 프레임 시각으로 `TryGetReadyWeapon(action, now, out selected)`를 호출한다. 반환된 원형의 `BasicAttack` 또는 `Skill` 참조를 그 발동에 사용한다. 이미 발동한 요청의 저장된 원형과 패턴은 계속 그 발동에 사용한다.
- 발동이 승인되면 `locks.RecordAccepted(action, selected.InstanceId, sharedReadyAt, weaponReadyAt)`으로 해당 실물의 잠금 시각을 기록한다. 두 시각은 동일 탐험의 절대 시각이다. 기본공격 스케줄러를 연결할 때는 그 스케줄러가 이미 사용하는 공통 대기 시각과 동일한 값을 사용한다. 스킬의 실행과 타이밍은 스킬 실행기가 제공한다. [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58)

같은 슬롯, 비어 있거나 범위를 벗어난 슬롯, 공격·스킬 동작 및 귀환 채널링 중의 입력은 무시한다. 다음 유효한 아이콘 입력부터 처리한다. 기본공격과 스킬은 각자의 공통 잠금 및 해당 실물의 준비 시각 중 늦은 쪽을 적용한다. 교체는 잠금을 그대로 유지한다.

## 재현

```powershell
unity test . --mode PlayMode --filter ProjectWeapon.WeaponSwitch.Tests --output "$env:TEMP/KAN-62-playmode.xml"
```

PlayMode에서 실제 uGUI 아이콘 클릭을 기존 자동공격 스케줄러에 연결해 다음 패턴을 검증한다. 스킬 참조 변경, 중복·빈 칸·유효 범위·동작 중·귀환 중 입력, 두 종류의 재사용 가능 시각 경계도 확인한다.

Unity 6000.6.9f1에서 해당 PlayMode 3건이 통과했다.
